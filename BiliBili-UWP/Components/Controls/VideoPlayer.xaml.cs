﻿using BiliBili_Lib.Enums;
using BiliBili_Lib.Models.BiliBili;
using BiliBili_Lib.Models.BiliBili.Anime;
using BiliBili_Lib.Models.BiliBili.Video;
using BiliBili_Lib.Service;
using BiliBili_Lib.Tools;
using BiliBili_UWP.Components.Widgets;
using BiliBili_UWP.Models.Enums;
using BiliBili_UWP.Models.UI;
using BiliBili_UWP.Models.UI.Others;
using Microsoft.Toolkit.Uwp.Helpers;
using NSDanmaku.Helper;
using NSDanmaku.Model;
using SYEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using Windows.System;
using Windows.System.Display;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace BiliBili_UWP.Components.Controls
{
    public sealed partial class VideoPlayer : UserControl
    {
        #region 集合
        private ObservableCollection<Tuple<int, string>> QualityCollection = new ObservableCollection<Tuple<int, string>>();
        private ObservableCollection<Choice> ChoiceCollection = new ObservableCollection<Choice>();
        private ObservableCollection<SystemFont> FontCollection = App.AppViewModel.FontCollection;
        private ObservableCollection<SubtitleIndexItem> SubtitleIndexCollection = new ObservableCollection<SubtitleIndexItem>();
        private List<DanmakuColor> DanmakuColors = DanmakuColor.GetColorList();
        private List<SubtitleItem> Subtitles = new List<SubtitleItem>();
        private List<DanmakuModel> DanmakuList = new List<DanmakuModel>();
        private List<string> ShieldTextLish = new List<string>(); //屏蔽关键字列表
        private List<string> SendDanmakuList = new List<string>();
        #endregion

        #region 变量
        private VideoPlayBase _playData = null;
        private int _currentQn = 0;
        private MediaPlayer _player = new MediaPlayer();
        private MediaSource _tempSource = null;
        private int _pointerHoldCount = 0; // 光标保持不动的持续时间
        private int _heartBeatCount = 0;
        private bool _isCatchPointer = false;
        private bool _isMergeSameDanmaku = false; //合并相同弹幕
        private int _maxDanmakuNumber = 0;
        public bool IsFocus = false;
        private DisplayRequest dispRequest = null;
        private InteractionVideo _interaction = null;
        private bool _isMediaElementLoaded = false;

        private int _videoId = 0;
        private int _partId = 0;
        private Episode _bangumiPart = null;

        private VideoDetail _videoDetail = null;
        private BangumiDetail _bangumiDetail = null;

        public bool isBangumi = false;
        private bool _isChoiceHandling = false;
        private bool _isMTCShow = false;

        public int _skipStep = 0;
        private bool _isDanmakuOptionsInit = false;

        private double _playRate = 1;
        private int _tipShowSeconds = 0;
        public int CurrentProgress
        {
            get => Convert.ToInt32(_player.PlaybackSession.Position.TotalSeconds);
        }

        private Point _manipulationStartPoint = new Point(0, 0);
        private double _manipulationDeltaX = 0;
        private double _manipulationDeltaY = 0;
        private double _manipulationProgress = 0;
        private double _manipulationVolume = 0;
        private bool _manipulationBeforeIsPlay = false;
        private PlayerManipulationType _manipulationType = PlayerManipulationType.None;
        #endregion

        #region 服务及控件
        private VideoService _videoService = App.BiliViewModel._client.Video;
        private AnimeService _animeService = App.BiliViewModel._client.Anime;

        private DanmakuParse _danmakuParse = new DanmakuParse();
        private NSDanmaku.Controls.Danmaku DanmakuControls = null;

        private DispatcherTimer _danmaTimer = new DispatcherTimer();
        private DispatcherTimer _subtitleTimer = new DispatcherTimer();

        public VideoTransportControls MTC;
        #endregion

        #region 事件
        public event EventHandler<bool> FullWindowChanged;
        public event EventHandler<bool> CinemaChanged;
        public event EventHandler<bool> CompactOverlayChanged;
        public event RoutedEventHandler SeparateButtonClick;
        public event EventHandler MTCLoaded;
        public event EventHandler<int> MediaEnded;
        #endregion

        public VideoPlayer()
        {
            this.InitializeComponent();
            _danmaTimer.Interval = TimeSpan.FromSeconds(1);
            _danmaTimer.Tick += DanmuTimer_Tick;
            _subtitleTimer.Interval = TimeSpan.FromSeconds(0.1);
            _subtitleTimer.Tick += SubtitleTimer_Tick;
            DanmakuControls = VideoMTC.DanmakuControls;
            MTC = VideoMTC;
        }

        #region 加载
        public async Task Init(VideoDetail detail, int cid = 0)
        {
            _videoDetail = detail;
            _videoId = detail.aid;
            isBangumi = false;
            if (cid == 0)
            {
                if (detail != null && detail.history != null)
                    cid = detail.history.cid;
                else if (detail != null && detail.pages != null)
                    cid = detail.pages.First().cid;
            }
            mediaElement.PosterSource = new BitmapImage(new Uri(detail.pic));

            if (detail.interaction != null)
            {
                int nodeId = 0;
                if (detail.interaction.history_node != null)
                {
                    cid = detail.interaction.history_node.cid;
                    nodeId = detail.interaction.history_node.node_id;
                }
                await InitInteraction(cid, nodeId);
            }
            else
            {
                Reset();
                int progress = 0;
                if (detail.history != null)
                    progress = detail.history.progress;
                await RefreshVideoSource(cid, progress);
            }
            UpdateMediaProperties(detail.title, "", detail.pic);
        }
        public async Task Init(BangumiDetail detail, Episode part)
        {
            isBangumi = true;
            _bangumiDetail = detail;
            _bangumiPart = part;
            Reset();
            mediaElement.PosterSource = new BitmapImage(new Uri(detail.cover));
            await RefreshVideoSource(part);
            UpdateMediaProperties(part.title, part.subtitle, detail.cover);
        }
        private void Reset()
        {
            ErrorContainer.Visibility = Visibility.Collapsed;
            ErrorBlock.Text = "嗨呀，加载失败啦！";
            InteractionHomeButton.Visibility = Visibility.Collapsed;

            bool isShowDanmaku = AppTool.GetBoolSetting(Settings.IsDanmakuOpen);
            DanmakuVisibilityButton.Content = isShowDanmaku ? "" : "";
            DanmakuColorItemsControl.ItemsSource = DanmakuColors;
            ColorTextBox.Text = "#FFFFFF";
            DefaultFontSizeRadio.IsChecked = true;
            ModeComboBox.SelectedIndex = 0;
            ColorViewBorder.Background = new SolidColorBrush(Windows.UI.Colors.White);
            DanmakuBox.Text = "";
            _skipStep = Convert.ToInt32(AppTool.GetLocalSetting(Settings.PlayerSkipStep, "30"));

            _playData = null;
            _currentQn = 0;
            _playRate = 1;
            QualityCollection.Clear();
            DanmakuList.Clear();
            SendDanmakuList.Clear();
            Subtitles.Clear();
            SubtitleIndexCollection.Clear();
            _isMTCShow = true;
            ShowMTC();
            HideSubtitle();
            if (mediaElement.MediaPlayer != null)
            {
                mediaElement.MediaPlayer.Pause();
                mediaElement.MediaPlayer.TimelineControllerPositionOffset = TimeSpan.FromSeconds(0);
                if (_tempSource != null)
                {
                    _tempSource.Dispose();
                }
            }
            _player = new MediaPlayer();
            double volume = Convert.ToDouble(AppTool.GetLocalSetting(Settings.PlayerLastVolume, "1"));
            _player.Volume = volume;
            bool isAutoPlay = AppTool.GetBoolSetting(Settings.IsAutoPlay);
            _player.AutoPlay = isAutoPlay;
            _player.MediaEnded += Media_Ended;
            _player.MediaFailed += Media_Failed;
            _player.MediaOpened += Media_Opened;
            _player.PlaybackSession.PositionChanged += Media_PositionChanged;
            _player.PlaybackSession.PlaybackStateChanged += Media_StatusChanged;
            _player.VolumeChanged += Volume_Changed;
            mediaElement.SetMediaPlayer(_player);
            if (DanmakuControls != null)
                DanmakuControls.ClearAll();
            _danmaTimer.Start();
        }
        #endregion

        #region 媒体播放事件
        private async void Media_PositionChanged(MediaPlaybackSession sender, object args)
        {
            await DispatcherHelper.ExecuteOnUIThreadAsync(() =>
            {
                if (_videoDetail != null && _videoDetail.interaction != null)
                {
                    InteractionEndContainer.Visibility = Visibility.Collapsed;
                    if (sender.Position < sender.NaturalDuration && ChoiceItemsControl.Visibility == Visibility.Visible)
                    {
                        ChoiceItemsControl.Visibility = Visibility.Collapsed;
                    }
                }
            });
        }

        private async void Media_StatusChanged(MediaPlaybackSession sender, object args)
        {
            await DispatcherHelper.ExecuteOnUIThreadAsync(() =>
            {
                if (_playData == null)
                    return;
                if (sender.PlaybackState == MediaPlaybackState.Paused && VideoMTC.IsPlaying)
                    VideoMTC.IsPlaying = false;
                else if (sender.PlaybackState == MediaPlaybackState.Playing && !VideoMTC.IsPlaying)
                    VideoMTC.IsPlaying = true;
            });
        }

        private async void Media_Opened(MediaPlayer sender, object args)
        {
            await DispatcherHelper.ExecuteOnUIThreadAsync(() =>
            {
                bool isAutoPlay = AppTool.GetBoolSetting(Settings.IsAutoPlay);
                if (isAutoPlay)
                {
                    MTC.IsPlaying = true;
                    _player.Play();
                    HideMTC();
                }
                ErrorContainer.Visibility = Visibility.Collapsed;
            });
        }

        private async void Media_Failed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            await DispatcherHelper.ExecuteOnUIThreadAsync(() =>
            {
                ErrorBlock.Text = args.ErrorMessage;
                ErrorContainer.Visibility = Visibility.Visible;
            });
        }

        private void Volume_Changed(MediaPlayer sender, object args)
        {
            AppTool.WriteLocalSetting(Settings.PlayerLastVolume, sender.Volume.ToString());
        }

        private async void Media_Ended(MediaPlayer sender, object args)
        {
            await DispatcherHelper.ExecuteOnUIThreadAsync(async () =>
            {
                VideoMTC.IsPlaying = false;
                if (_videoDetail.interaction != null)
                {
                    //互动视频
                    if (ChoiceCollection.Count > 0)
                    {
                        if (ChoiceCollection.Count == 1)
                            await InitInteraction(ChoiceCollection.First().cid, ChoiceCollection.First().id);
                        else
                            ChoiceItemsControl.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        InteractionEndContainer.Visibility = Visibility.Visible;
                        MediaEnded?.Invoke(this, _videoId);
                    }
                }
                else
                {
                    if (IsAutoReturnWhenEnd)
                    {
                        if (VideoMTC.IsFullWindow)
                            VideoMTC.IsFullWindow = false;
                        else if (VideoMTC.IsCinema)
                            VideoMTC.IsCinema = false;
                        else if (VideoMTC.IsCompactOverlay)
                            VideoMTC.IsCompactOverlay = false;
                    }
                    int id = 0;
                    if (isBangumi)
                        id = _bangumiPart.id;
                    else
                        id = _videoId;
                    MediaEnded?.Invoke(this, id);
                }
            });
        }
        #endregion

        #region 播放及刷新
        public async Task RefreshVideoSource(int partId, int progress = 0)
        {
            LoadingBar.Visibility = Visibility.Visible;
            var _mediaPlayer = mediaElement.MediaPlayer;
            if (_playData == null || _partId != partId)
            {
                _partId = partId;
                var data = await _videoService.GetVideoPlayAsync(_videoId, partId, _currentQn);
                if (data != null)
                {
                    _playData = data;
                    for (int i = 0; i < _playData.accept_quality.Count; i++)
                    {
                        QualityCollection.Add(new Tuple<int, string>(_playData.accept_quality[i], _playData.accept_description[i]));
                    }
                    VideoMTC._qualityListView.SelectedIndex = -1;
                    int firstQn = Convert.ToInt32(AppTool.GetLocalSetting(Settings.FirstQuality, "0"));
                    if (firstQn > 0)
                    {
                        for (int i = 0; i < QualityCollection.Count; i++)
                        {
                            if (QualityCollection[i].Item1 == firstQn)
                            {
                                _currentQn = firstQn;
                                VideoMTC._qualityListView.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                    if (_currentQn == 0)
                    {
                        _currentQn = QualityCollection.First().Item1;
                        VideoMTC._qualityListView.SelectedIndex = 0;
                    }
                }
            }
            if (_playData != null)
            {
                await LoadDanmaku();
                await LoadSubtitle();
                MediaSource mediaSource = null;
                if (_playData is VideoPlayDash)
                    mediaSource = await HandleDashSource();
                else
                    mediaSource = await HandleFlvSource(_videoId);
                if (mediaSource != null)
                {
                    TimeSpan offset = TimeSpan.FromSeconds(progress);
                    if (progress == 0)
                        offset = TimeSpan.FromSeconds(_mediaPlayer.PlaybackSession.Position.TotalSeconds);
                    var other = _tempSource;
                    _tempSource = mediaSource;
                    _mediaPlayer.Source = new MediaPlaybackItem(mediaSource);
                    if (offset.TotalSeconds > 0)
                        _player.PlaybackSession.Position = offset;
                    other?.Dispose();
                    VideoMTC.IsInit = false;
                    VideoMTC.IsPlaying = _player.AutoPlay;
                    VideoMTC.IsInit = true;
                    if (_player.AutoPlay)
                        Resume();
                }
                else
                    ErrorContainer.Visibility = Visibility.Visible;
            }
            else
            {
                ErrorContainer.Visibility = Visibility.Visible;
            }
            mediaElement.Focus(FocusState.Programmatic);
            LoadingBar.Visibility = Visibility.Collapsed;
        }
        public async Task RefreshVideoSource(Episode part)
        {
            if (part == null)
            {
                ErrorContainer.Visibility = Visibility.Visible;
                return;
            }
            LoadingBar.Visibility = Visibility.Visible;
            if (_playData == null || _bangumiPart.id != part.id)
            {
                _bangumiPart = part;
                var data = await _animeService.GetBangumiPlayAsync(_bangumiDetail.type, part.cid, _currentQn);
                if (data != null)
                {
                    _playData = data;
                    for (int i = 0; i < _playData.accept_quality.Count; i++)
                    {
                        QualityCollection.Add(new Tuple<int, string>(_playData.accept_quality[i], _playData.accept_description[i]));
                    }
                    int firstQn = Convert.ToInt32(AppTool.GetLocalSetting(Settings.FirstQuality, "0"));
                    if (firstQn > 0)
                    {
                        for (int i = 0; i < QualityCollection.Count; i++)
                        {
                            if (QualityCollection[i].Item1 == firstQn)
                            {
                                _currentQn = firstQn;
                                VideoMTC._qualityListView.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                    if (_currentQn == 0)
                        _currentQn = QualityCollection.First().Item1;
                    VideoMTC.QualitySelectIndex = 0;
                }
            }
            if (_playData != null)
            {
                await LoadDanmaku();
                MediaSource mediaSource = null;
                if (_playData is VideoPlayDash)
                    mediaSource = await HandleDashSource();
                else
                    mediaSource = await HandleFlvSource(_videoId, true);
                var other = _tempSource;
                _tempSource = mediaSource;
                _player.Source = new MediaPlaybackItem(mediaSource);
                other?.Dispose();

                VideoMTC.IsInit = false;
                VideoMTC.IsPlaying = _player.AutoPlay;
                VideoMTC.IsInit = true;
                if (_player.AutoPlay)
                    Resume();
            }
            else
            {
                ErrorContainer.Visibility = Visibility.Visible;
            }
            mediaElement.Focus(FocusState.Programmatic);
            LoadingBar.Visibility = Visibility.Collapsed;
        }
        private async Task InitInteraction(int cid, int edgeId)
        {
            if (_isChoiceHandling)
                return;
            _isChoiceHandling = true;
            Reset();
            InteractionHomeButton.Visibility = Visibility.Visible;
            var data = await _videoService.GetInteractionVideoAsync(_videoId, _videoDetail.interaction.graph_version, edgeId);
            ChoiceItemsControl.Visibility = Visibility.Collapsed;
            await RefreshVideoSource(cid);
            if (data != null)
            {
                ChoiceCollection.Clear();
                _interaction = data;
                if (_interaction.edges.questions != null)
                {
                    var choices = _interaction.edges.questions.First().choices;
                    choices.ForEach(p => ChoiceCollection.Add(p));
                }
            }
            _isChoiceHandling = false;
        }
        /// <summary>
        /// 更新SMTC的显示
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="subtitle">副标题</param>
        /// <param name="cover">封面</param>
        private void UpdateMediaProperties(string title, string subtitle, string cover)
        {
            SystemMediaTransportControlsDisplayUpdater updater = _player.SystemMediaTransportControls.DisplayUpdater;
            updater.Type = MediaPlaybackType.Video;
            updater.VideoProperties.Title = title;
            updater.VideoProperties.Subtitle = subtitle;
            updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(cover));
            updater.Update();
        }
        #endregion

        #region 弹幕
        private async Task LoadDanmaku()
        {
            bool isShow = AppTool.GetBoolSetting(Settings.IsDanmakuOpen);
            DanmakuList.Clear();
            if (isShow)
            {
                DanmakuControls.Visibility = Visibility.Visible;
                if (isBangumi)
                    DanmakuList = await _danmakuParse.ParseBiliBili(Convert.ToInt64(_bangumiPart.cid));
                else
                    DanmakuList = await _danmakuParse.ParseBiliBili(Convert.ToInt64(_partId));
            }
            else
            {
                DanmakuControls.Visibility = Visibility.Collapsed;
            }
        }
        private void DanmuTimer_Tick(object sender, object e)
        {
            if (_pointerHoldCount >= 2)
            {
                bool isManual = AppTool.GetBoolSetting(Settings.IsManualMediaTransportControls, false);
                if (_isCatchPointer && Window.Current.CoreWindow.PointerCursor != null)
                    Window.Current.CoreWindow.PointerCursor = null;
                if (_isMTCShow && !isManual)
                {
                    _isMTCShow = false;
                    HideMTC();
                }
            }
            if (_pointerHoldCount < 2)
                _pointerHoldCount++;
            if (_tipShowSeconds >= 2)
                HideTip();
            else if (_tipShowSeconds != -1)
                _tipShowSeconds++;
            if (_heartBeatCount >= 10)
            {
                HeartBeat();
                _heartBeatCount = 0;
            }
            else
                _heartBeatCount++;
            try
            {
                if (DanmakuControls == null)
                    return;
                if (mediaElement.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing && DanmakuList.Count > 0)
                {
                    int nowDanmaNum = 0;
                    var currentPosition = mediaElement.MediaPlayer.PlaybackSession.Position.TotalSeconds;
                    var tempList = DanmakuList.Where(p => p.time > currentPosition && p.time - currentPosition < 1).ToList();
                    if (tempList.Count > 0)
                    {
                        foreach (var item in tempList)
                        {
                            if (nowDanmaNum >= _maxDanmakuNumber && _maxDanmakuNumber != 0)
                                break;
                            if (!IsTextShouldShield(item.text))
                            {
                                switch (item.location)
                                {
                                    case DanmakuLocation.Top:
                                        DanmakuControls.AddTopDanmu(item, false);
                                        break;
                                    case DanmakuLocation.Bottom:
                                        DanmakuControls.AddBottomDanmu(item, false);
                                        break;
                                    case DanmakuLocation.Position:
                                        DanmakuControls.AddPositionDanmu(item);
                                        break;
                                    default:
                                        DanmakuControls.AddRollDanmu(item, false);
                                        break;
                                }
                                nowDanmaNum++;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {

            }
        }
        /// <summary>
        /// 弹幕是否该被屏蔽（不显示）
        /// </summary>
        /// <param name="text">弹幕文本</param>
        /// <param name="location">弹幕位置</param>
        /// <returns></returns>
        private bool IsTextShouldShield(string text, DanmakuLocation location = DanmakuLocation.Other)
        {
            if (ShieldTextLish.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (_isMergeSameDanmaku && SendDanmakuList.Contains(text + location))
                return true;
            return false;
        }
        private async void DanmakuVisibilityButton_Click(object sender, RoutedEventArgs e)
        {
            bool isShowDanmaku = AppTool.GetBoolSetting(Settings.IsDanmakuOpen);
            isShowDanmaku = !isShowDanmaku;
            AppTool.WriteLocalSetting(Settings.IsDanmakuOpen, isShowDanmaku.ToString());
            DanmakuVisibilityButton.Content = isShowDanmaku ? "" : "";
            await LoadDanmaku();
        }

        private async void SendDanmakuButton_Click(object sender, RoutedEventArgs e)
        {
            await SendDanmaku();
        }

        private async void DanmakuBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                await SendDanmaku();
            }
        }

        private async Task SendDanmaku()
        {
            if (!App.BiliViewModel.CheckAccoutStatus())
                return;
            if (_playData == null || _player.PlaybackSession.PlaybackState == MediaPlaybackState.Opening)
            {
                new TipPopup("请等待视频加载完成").ShowMessage();
                return;
            }
            string text = DanmakuBox.Text;
            if (!string.IsNullOrEmpty(text))
            {
                SendDanmakuButton.IsEnabled = false;
                DanmakuBox.IsEnabled = false;
                string color = "";
                if (!string.IsNullOrEmpty(ColorTextBox.Text))
                    color = UIHelper.GetDanmakuColor(ColorHelper.ToColor(ColorTextBox.Text));
                else
                    color = UIHelper.GetDanmakuColor(Windows.UI.Colors.White);
                string fontSize = Convert.ToBoolean(DefaultFontSizeRadio.IsChecked) ? "25" : "20";
                string mode = (ModeComboBox.SelectedItem as ComboBoxItem).Tag.ToString();
                bool result = false;
                double progress = _player.PlaybackSession.Position.TotalMilliseconds;
                if (isBangumi)
                    result = await _videoService.SendDanmakuAsync(text, _bangumiPart.aid, _bangumiPart.cid, progress, color, fontSize, mode);
                else
                    result = await _videoService.SendDanmakuAsync(text, _videoId, _partId, progress, color, fontSize, mode);
                if (result)
                {
                    DanmakuBox.Text = string.Empty;
                    int fontSizeNum = Convert.ToInt32(fontSize);
                    if (mode == "1")
                        DanmakuControls.AddRollDanmu(new DanmakuModel { color = ColorHelper.ToColor(ColorTextBox.Text), size = fontSizeNum, text = text }, true);
                    else if (mode == "4")
                        DanmakuControls.AddBottomDanmu(new DanmakuModel { color = ColorHelper.ToColor(ColorTextBox.Text), size = fontSizeNum, text = text }, true);
                    else if (mode == "5")
                        DanmakuControls.AddTopDanmu(new DanmakuModel { color = ColorHelper.ToColor(ColorTextBox.Text), size = fontSizeNum, text = text }, true);
                    else
                        DanmakuControls.AddRollDanmu(new DanmakuModel { color = ColorHelper.ToColor(ColorTextBox.Text), size = fontSizeNum, text = text }, true);
                }
                else
                {
                    new TipPopup("发送失败").ShowError();
                }
                // send
                SendDanmakuButton.IsEnabled = true;
                DanmakuBox.IsEnabled = true;
            }
        }
        #endregion

        #region 弹幕设置
        private void DanmakuColor_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var data = (sender as FrameworkElement).DataContext as DanmakuColor;
            ColorTextBox.Text = data.Color.Color.ToString();
            ColorViewBorder.Background = data.Color;
        }

        private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                double v = OpacitySlider.Value;
                DanmakuControls.Opacity = v;
                AppTool.WriteLocalSetting(Settings.DanmakuOpacity, v.ToString());
            }
        }

        private void FontSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                double v = FontSizeSlider.Value;
                DanmakuControls.sizeZoom = v;
                AppTool.WriteLocalSetting(Settings.DanmakuFontSize, v.ToString());
            }
        }

        private void SpeedSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                double v = SpeedSlider.Value;
                DanmakuControls.speed = 25 - Convert.ToInt32(v * 12);
                AppTool.WriteLocalSetting(Settings.DanmakuSpeed, v.ToString());
            }
        }

        private void MaxinumSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                double v = MaxinumSlider.Value;
                _maxDanmakuNumber = Convert.ToInt32(v);
                AppTool.WriteLocalSetting(Settings.DanmakuMaxNumber, v.ToString("0"));
            }
        }
        private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                var item = FontComboBox.SelectedItem as SystemFont;
                string oldFont = AppTool.GetLocalSetting(Settings.DanmakuFontFamily, "微软雅黑");
                if (item.Name != oldFont)
                {
                    AppTool.WriteLocalSetting(Settings.DanmakuFontFamily, item.Name);
                    DanmakuControls.font = item.Name;
                }
            }
        }

        private void MergeDanmakuSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                bool isMerge = MergeDanmakuSwitch.IsOn;
                _isMergeSameDanmaku = isMerge;
                AppTool.WriteLocalSetting(Settings.DanmakuMerge, isMerge.ToString());
            }
        }

        private void ProtectSubtitleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                bool isProtect = ProtectSubtitleSwitch.IsOn;
                DanmakuControls.notHideSubtitle = isProtect;
                AppTool.WriteLocalSetting(Settings.DanmakuProtectSubtitle, isProtect.ToString());
            }
        }

        private void BorderStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                int index = BorderStyleComboBox.SelectedIndex;
                if (index == -1)
                    return;
                DanmakuControls.borderStyle = (DanmakuBorderStyle)index;
                AppTool.WriteLocalSetting(Settings.DanmakuBorderStyle, index.ToString());
            }
        }
        #endregion

        #region 字幕
        private async Task LoadSubtitle()
        {
            SubtitleIndexCollection.Clear();
            var index = await _videoService.GetVideoSubtitleIndexAsync(_videoId, _partId);
            if (index != null)
            {
                index.ForEach(p => SubtitleIndexCollection.Add(p));
            }
            if (SubtitleIndexCollection.Count > 0)
            {
                SubtitleIndexCollection.Insert(0, SubtitleIndexItem.UnSelected);
                VideoMTC._subtitleListView.SelectedIndex = -1;
                await SubtitleInit(SubtitleIndexCollection[1]);
                VideoMTC._subtitleListView.SelectedIndex = 1;
                VideoMTC.SubtitleHolderVisibility = Visibility.Collapsed;
            }
            else
                VideoMTC.SubtitleHolderVisibility = Visibility.Visible;
        }

        private async Task SubtitleInit(SubtitleIndexItem item)
        {
            _subtitleTimer.Stop();
            Subtitles.Clear();
            HideSubtitle();
            if (string.IsNullOrEmpty(item.subtitle_url))
                return;
            var response = await _videoService.GetSubtitlesAsync(item.subtitle_url);
            var subtitles = response.body;
            if (subtitles != null)
            {
                subtitles.ForEach(p => Subtitles.Add(p));
                _subtitleTimer.Start();
            }
            else
            {
                new TipPopup("字幕加载异常").ShowError();
            }
        }
        private void SubtitleTimer_Tick(object sender, object e)
        {
            if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                var time = _player.PlaybackSession.Position.TotalSeconds;
                var subtitle = Subtitles.FirstOrDefault(p => p.from <= time && p.to >= time);
                if (subtitle != null && !string.IsNullOrEmpty(subtitle.content))
                {
                    ShowSubtitle(subtitle.content);
                }
                else
                {
                    HideSubtitle();
                }
            }
        }

        private void ShowSubtitle(string content)
        {
            SubtitleContainer.Visibility = Visibility.Visible;
            SubtitleContentBlock.Text = content;
        }

        private void HideSubtitle()
        {
            SubtitleContainer.Visibility = Visibility.Collapsed;
            SubtitleContentBlock.Text = string.Empty;
        }
        #endregion

        #region 播放器状态控制
        public void Pause()
        {
            VideoMTC.IsPlaying = false;
            _danmaTimer.Stop();
            if (dispRequest != null)
            {
                dispRequest.RequestRelease();
            }
            dispRequest = null;
        }

        public void Resume()
        {
            VideoMTC.IsPlaying = true;
            _danmaTimer.Start();
            if (dispRequest == null)
            {
                // 用户观看视频，需要保持屏幕的点亮状态
                dispRequest = new DisplayRequest();
            }
            dispRequest.RequestActive();
        }

        public void Close()
        {
            Pause();
            DanmakuList.Clear();
            if (_tempSource != null)
                _tempSource.Dispose();
        }
        public void SkipForward()
        {
            if (_player != null && _player.PlaybackSession.CanSeek)
            {
                var position = _player.PlaybackSession.Position.TotalSeconds;
                var total = _player.PlaybackSession.NaturalDuration.TotalSeconds;
                double target = 0d;
                if (position + _skipStep > total)
                    target = total;
                else
                    target = position + _skipStep;
                _player.PlaybackSession.Position = TimeSpan.FromSeconds(target);
            }
        }
        public void SkipRewind()
        {
            if (_player != null && _player.PlaybackSession.CanSeek)
            {
                var position = _player.PlaybackSession.Position.TotalSeconds;
                double target = 0d;
                if (position - _skipStep < 0)
                    target = 0;
                else
                    target = position - _skipStep;
                _player.PlaybackSession.Position = TimeSpan.FromSeconds(target);
            }
        }
        private void MediaPresenter_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            _manipulationVolume = 0;
            _manipulationProgress = 0;
            _manipulationDeltaX = 0;
            _manipulationDeltaY = 0;
            _manipulationStartPoint = new Point(0, 0);
            _manipulationType = PlayerManipulationType.None;
            if (_manipulationBeforeIsPlay)
                Resume();
            _manipulationBeforeIsPlay = false;
        }

        private void MediaPresenter_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            _manipulationDeltaX += e.Delta.Translation.X;
            _manipulationDeltaY -= e.Delta.Translation.Y;
            Debug.WriteLine(_manipulationDeltaX);
            Debug.WriteLine(_manipulationDeltaY);
            if (Math.Abs(_manipulationDeltaX) > 15 || Math.Abs(_manipulationDeltaY) > 15)
            {
                if (_manipulationType == PlayerManipulationType.None)
                {
                    bool isVolume = Math.Abs(_manipulationDeltaY) > Math.Abs(_manipulationDeltaX);
                    _manipulationType = isVolume ? PlayerManipulationType.Volume : PlayerManipulationType.Progress;
                }
                if (_manipulationType == PlayerManipulationType.Volume)
                {
                    var volume = _manipulationVolume + _manipulationDeltaY / 2.0;
                    if (volume > 100)
                        volume = 100;
                    else if (volume < 0)
                        volume = 0;
                    ShowTip($"当前音量: {Math.Round(volume)}");
                    _player.Volume = volume / 100.0;
                    if (volume == 0)
                        Debug.WriteLine("静音了！");
                }
                else
                {
                    var progress = _manipulationProgress + (_manipulationDeltaX / 2.5);
                    if (progress > _player.PlaybackSession.NaturalDuration.TotalSeconds)
                        progress = _player.PlaybackSession.NaturalDuration.TotalSeconds;
                    else if (progress < 0)
                        progress = 0;
                    ShowTip($"当前进度: {AppTool.GetReadDuration(Convert.ToInt32(progress))}");
                    _player.PlaybackSession.Position = TimeSpan.FromSeconds(progress);
                }
            }
        }

        private void MediaPresenter_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _manipulationStartPoint = e.Position;
            _manipulationProgress = CurrentProgress;
            _manipulationVolume = _player.Volume * 100.0;
            _manipulationBeforeIsPlay = VideoMTC.IsPlaying;
            Pause();
        }
        #endregion

        #region 播放源处理
        private async Task<MediaSource> HandleDashSource()
        {
            bool isHevc = AppTool.GetBoolSetting(Settings.IsUseHevc, false);
            var data = _playData as VideoPlayDash;
            int codecId = isHevc ? 12 : 7;
            var video = data.dash.video.FirstOrDefault(p => p.id == _currentQn && p.codecid == codecId);
            if (video == null && codecId == 12)
                video = data.dash.video.FirstOrDefault(p => p.id == _currentQn);
            if (video == null)
                video = data.dash.video.OrderByDescending(p => p.id).FirstOrDefault(p => p.codecid == 7);
            var audio = data.dash.audio.FirstOrDefault();
            MediaSource source = null;
            if (isBangumi)
                source = await _animeService.CreateMediaSourceAsync(video, audio);
            else
                source = await _videoService.CreateMediaSourceAsync(video, audio);
            return source;
        }
        private async Task<MediaSource> HandleFlvSource(int videoId, bool isBangumi = false)
        {
            var playList = new Playlist(PlaylistTypes.NetworkHttp);
            var data = _playData as VideoPlayFlv;
            List<string> urls = new List<string>();
            data.durl.ForEach(p =>
            {
                urls.Add(p.url);
                playList.Append(p.url, 0, p.length / 1000);
            });
            string prefix = isBangumi ? "https://www.bilibili.com/bangumi/play/ep" : "https://www.bilibili.com/video/av";
            playList.NetworkConfigs = CreatePlaylistNetworkConfigs("https://www.bilibili.com/video/av" + videoId + "/");
            var mediaSouce = MediaSource.CreateFromUri(await playList.SaveAndGetFileUriAsync());
            return mediaSouce;
        }

        private PlaylistNetworkConfigs CreatePlaylistNetworkConfigs(string referer)
        {
            PlaylistNetworkConfigs config = new PlaylistNetworkConfigs();
            config.DownloadRetryOnFail = true;
            config.HttpCookie = string.Empty;
            config.UniqueId = string.Empty;
            config.HttpUserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/69.0.3497.100 Safari/537.36";
            config.HttpReferer = referer;
            return config;
        }
        #endregion

        #region 控件事件处理
        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DanmakuControls != null)
                DanmakuControls.Clip = new RectangleGeometry() { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        }

        private void UserControl_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isCatchPointer = true;
        }

        private void UserControl_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isCatchPointer = false;
            Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 0);
        }

        private void UserControl_GotFocus(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("已获取焦点");
            IsFocus = true;
        }

        private void UserControl_LostFocus(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("失去焦点");
            IsFocus = false;
        }
        private void UserControl_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            _pointerHoldCount = 0;
            bool isManual = AppTool.GetBoolSetting(Settings.IsManualMediaTransportControls, false);
            if (!_isMTCShow && !isManual)
            {
                ShowMTC();
            }
            Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 0);
        }
        private void mediaElement_Tapped(object sender, TappedRoutedEventArgs e)
        {
            bool isManual = AppTool.GetBoolSetting(Settings.IsManualMediaTransportControls, false);
            if (isManual)
            {
                _isMTCShow = !_isMTCShow;
                if (_isMTCShow)
                    ShowMTC();
                else
                    HideMTC();
            }
        }

        private void mediaElement_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            e.Handled = true;
            VideoMTC.IsPlaying = !VideoMTC.IsPlaying;
        }
        private async void Choice_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var data = (sender as FrameworkElement).DataContext as Choice;
            await InitInteraction(data.cid, data.id);
        }
        private void ChoiceItemsControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ((sender as ItemsControl).ItemsPanelRoot as ItemsWrapGrid).ItemWidth = e.NewSize.Width / 2;
        }
        private async void InteractionHomeButton_Click(object sender, RoutedEventArgs e)
        {
            ChoiceCollection.Clear();
            ChoiceItemsControl.Visibility = Visibility.Collapsed;
            await InitInteraction(_videoDetail.pages.First().cid, 0);
        }
        private void ExitScreenButton_Click(object sender, RoutedEventArgs e)
        {
            ExitCurrentStatus();
        }
        #endregion

        #region MTC事件
        private void VideoMTC_DanmakuLoaded(object sender, NSDanmaku.Controls.Danmaku e)
        {
            DanmakuControls = e;
            VideoMTC.MediaPlayerElement = mediaElement;

            _isDanmakuOptionsInit = false;
            _maxDanmakuNumber = Convert.ToInt32(AppTool.GetLocalSetting(Settings.DanmakuMaxNumber, "200"));
            MaxinumSlider.Value = _maxDanmakuNumber;
            double danmakuOpacity = Convert.ToDouble(AppTool.GetLocalSetting(Settings.DanmakuOpacity, "1.0"));
            OpacitySlider.Value = danmakuOpacity;
            DanmakuControls.Opacity = danmakuOpacity;
            double danmakuSize = Convert.ToDouble(AppTool.GetLocalSetting(Settings.DanmakuFontSize, "1.0"));
            FontSizeSlider.Value = danmakuSize;
            DanmakuControls.sizeZoom = danmakuSize;
            double danmakuSpeed = Convert.ToDouble(AppTool.GetLocalSetting(Settings.DanmakuSpeed, "1.0"));
            SpeedSlider.Value = danmakuSpeed;
            DanmakuControls.speed = 25 - Convert.ToInt32(12 * danmakuSpeed);
            FontInit();
            bool isProtect = AppTool.GetBoolSetting(Settings.DanmakuProtectSubtitle, false);
            ProtectSubtitleSwitch.IsOn = isProtect;
            DanmakuControls.notHideSubtitle = isProtect;
            bool isMerge = AppTool.GetBoolSetting(Settings.DanmakuMerge, false);
            _isMergeSameDanmaku = isMerge;
            MergeDanmakuSwitch.IsOn = isMerge;
            int borderStyle = Convert.ToInt32(AppTool.GetLocalSetting(Settings.DanmakuBorderStyle, "0"));
            DanmakuControls.borderStyle = (DanmakuBorderStyle)borderStyle;
            BorderStyleComboBox.SelectedIndex = borderStyle;

            DanmakuControls.InitializeDanmaku(DanmakuMode.Video, danmakuSize, 25 - Convert.ToInt32(12 * danmakuSpeed), (DanmakuBorderStyle)borderStyle);

            _isDanmakuOptionsInit = true;
        }
        private void VideoMTC_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isMediaElementLoaded)
            {
                mediaElement.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
                mediaElement.ManipulationStarted += MediaPresenter_ManipulationStarted;
                mediaElement.ManipulationDelta += MediaPresenter_ManipulationDelta;
                mediaElement.ManipulationCompleted += MediaPresenter_ManipulationCompleted;
                _isMediaElementLoaded = true;
            }
            MTCLoaded?.Invoke(this, EventArgs.Empty);
        }
        private void VideoMTC_PlayButtonClick(object sender, bool e)
        {
            if (e)
                _danmaTimer.Start();
            else
                _danmaTimer.Stop();
            this.Focus(FocusState.Programmatic);
        }
        private void VideoMTC_FullWindowChanged(object sender, bool e)
        {
            bool isShowDanmakuBar = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInFullWindow, false);
            DanmakuBarVisibility = isShowDanmakuBar ? Visibility.Visible : Visibility.Collapsed;
            ExitScreenButton.Visibility = e ? Visibility.Visible : Visibility.Collapsed;
            ChangeDanmakuBarDisplayMode(!e, isShowDanmakuBar);
            FullWindowChanged?.Invoke(this, e);
        }
        private void VideoMTC_CinemaChanged(object sender, bool e)
        {
            bool isShowDanmakuBar = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInCinema, false);
            DanmakuBarVisibility = isShowDanmakuBar ? Visibility.Visible : Visibility.Collapsed;
            ExitScreenButton.Visibility = e ? Visibility.Visible : Visibility.Collapsed;
            ChangeDanmakuBarDisplayMode(!e, isShowDanmakuBar);
            CinemaChanged?.Invoke(this, e);
        }
        private async void VideoMTC_QualityChanged(object sender, int e)
        {
            if (_currentQn != e)
            {
                _currentQn = e;
                AppTool.WriteLocalSetting(Settings.FirstQuality, e.ToString());
                await RefreshVideoSource(_partId);
            }
        }
        private void VideoMTC_CompactOverlayButtonClick(object sender, bool e)
        {
            bool isShowDanmakuBar = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInCompactOverlay, false);
            DanmakuBarVisibility = isShowDanmakuBar ? Visibility.Visible : Visibility.Collapsed;
            ExitScreenButton.Visibility = e ? Visibility.Visible : Visibility.Collapsed;
            ChangeDanmakuBarDisplayMode(!e, isShowDanmakuBar);
            CompactOverlayChanged?.Invoke(this, e);
        }
        private void VideoMTC_SeparateButtonClick(object sender, RoutedEventArgs e)
        {
            if (VideoMTC.IsCompactOverlay)
                VideoMTC.IsCompactOverlay = false;
            if (VideoMTC.IsFullWindow)
                VideoMTC.IsFullWindow = false;
            if (VideoMTC.IsCinema)
                VideoMTC.IsCinema = false;
            ChangeDanmakuBarDisplayMode(true);
            SeparateButtonClick?.Invoke(this, e);
        }
        private async void VideoMTC_SubtitleChanged(object sender, SubtitleIndexItem e)
        {
            await SubtitleInit(e);
        }
        private void VideoMTC_ForwardButtonClick(object sender, RoutedEventArgs e)
        {
            if (_playData != null && _player != null && _player.PlaybackSession != null)
            {
                if (_playRate < 2)
                    _playRate += 0.25;
                else
                {
                    ShowTip("已达2倍的最大播放倍率");
                    return;
                }
                ShowTip($"播放倍率：{_playRate}");
                _player.PlaybackSession.PlaybackRate = _playRate;
            }
        }
        private void VideoMTC_RewindButtonClick(object sender, RoutedEventArgs e)
        {
            if (_playData != null && _player != null && _player.PlaybackSession != null)
            {
                if (_playRate > 0.5)
                    _playRate -= 0.25;
                else
                {
                    ShowTip("已达0.5倍的最小播放倍率");
                    return;
                }
                if (_playRate < 0.5)
                    _playRate = 0.5;
                ShowTip($"播放倍率：{_playRate}");
                _player.PlaybackSession.PlaybackRate = _playRate;
            }
        }
        #endregion

        #region MTC状态控制
        public void HideMTC()
        {
            _isMTCShow = false;
            VideoMTC.Hide();
            ExitScreenButton.Visibility = Visibility.Collapsed;
            SubtitleContainer.Margin = new Thickness(20, 0, 20, 20);
            TipContainer.Margin = new Thickness(20, 0, 20, 20);
            if (MTC.IsFullWindow || MTC.IsCinema || MTC.IsCompactOverlay)
                DanmakuBarVisibility = Visibility.Collapsed;
        }

        public void ShowMTC()
        {
            bool isShowBarInFullWindow = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInFullWindow);
            bool isShowBarInCinema = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInCinema);
            bool isShowBarInCompact = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInCompactOverlay);
            _isMTCShow = true;
            if (VideoMTC.IsFullWindow || VideoMTC.IsCinema || VideoMTC.IsCompactOverlay)
                ExitScreenButton.Visibility = Visibility.Visible;
            SubtitleContainer.Margin = new Thickness(20, 0, 20, 100);
            VideoMTC.Show();
            if ((MTC.IsFullWindow && isShowBarInFullWindow) || (MTC.IsCinema && isShowBarInCinema) || (MTC.IsCompactOverlay && isShowBarInCompact))
                DanmakuBarVisibility = Visibility.Visible;
        }
        #endregion

        #region 依赖属性
        public Visibility CinemaButtonVisibility
        {
            get { return (Visibility)GetValue(CinemaButtonVisibilityProperty); }
            set { SetValue(CinemaButtonVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for CinemaButtonVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty CinemaButtonVisibilityProperty =
            DependencyProperty.Register("CinemaButtonVisibility", typeof(Visibility), typeof(VideoPlayer), new PropertyMetadata(Visibility.Visible));

        public Visibility FullWindowButtonVisibility
        {
            get { return (Visibility)GetValue(FullWindowButtonVisibilityProperty); }
            set { SetValue(FullWindowButtonVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for FullWindowButtonVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty FullWindowButtonVisibilityProperty =
            DependencyProperty.Register("FullWindowButtonVisibility", typeof(Visibility), typeof(VideoPlayer), new PropertyMetadata(Visibility.Visible));

        public Visibility CompactOverlayButtonVisibility
        {
            get { return (Visibility)GetValue(CompactOverlayButtonVisibilityProperty); }
            set { SetValue(CompactOverlayButtonVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for CompactOverlayButtonVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty CompactOverlayButtonVisibilityProperty =
            DependencyProperty.Register("CompactOverlayButtonVisibility", typeof(Visibility), typeof(VideoPlayer), new PropertyMetadata(Visibility.Visible));

        public Visibility SeparateButtonVisibility
        {
            get { return (Visibility)GetValue(SeparateButtonVisibilityProperty); }
            set { SetValue(SeparateButtonVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SeparateButtonVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SeparateButtonVisibilityProperty =
            DependencyProperty.Register("SeparateButtonVisibility", typeof(Visibility), typeof(VideoPlayer), new PropertyMetadata(Visibility.Visible));
        public Visibility DanmakuBarVisibility
        {
            get { return (Visibility)GetValue(DanmakuBarVisibilityProperty); }
            set { SetValue(DanmakuBarVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for DanmakuBarVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty DanmakuBarVisibilityProperty =
            DependencyProperty.Register("DanmakuBarVisibility", typeof(Visibility), typeof(VideoPlayer), new PropertyMetadata(Visibility.Visible));

        /// <summary>
        /// 播放完成后是否自动解除全屏/影院/小窗模式
        /// </summary>
        public bool IsAutoReturnWhenEnd
        {
            get { return (bool)GetValue(IsAutoReturnWhenEndProperty); }
            set { SetValue(IsAutoReturnWhenEndProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsAutoReturnWhenEnd.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsAutoReturnWhenEndProperty =
            DependencyProperty.Register("IsAutoReturnWhenEnd", typeof(bool), typeof(VideoPlayer), new PropertyMetadata(true));


        #endregion

        #region 其它
        private async void HeartBeat()
        {
            if (isBangumi)
                await _animeService.AddVideoHistoryAsync(_bangumiPart.aid, _bangumiPart.id, _bangumiPart.cid, CurrentProgress);
            else
                await _videoService.AddVideoHistoryAsync(_videoId, _partId, CurrentProgress);
        }
        private void FontInit()
        {
            FontComboBox.IsEnabled = false;
            if (FontCollection != null && FontCollection.Count > 0)
            {
                string fontName = AppTool.GetLocalSetting(Settings.DanmakuFontFamily, "微软雅黑");
                var font = FontCollection.Where(p => p.Name == fontName).FirstOrDefault();
                if (font != null)
                {
                    FontComboBox.SelectedItem = font;
                }
            }
            FontComboBox.IsEnabled = true;
        }
        public async void ResetDanmakuStatus()
        {
            bool isShow = AppTool.GetBoolSetting(Settings.IsDanmakuOpen);
            if (isShow)
            {
                DanmakuControls.Width = 100;
                await Task.Delay(100);
                DanmakuControls.Width = double.NaN;
            }
        }
        public bool ExitCurrentStatus()
        {
            bool isHandled = true;
            if (VideoMTC.IsFullWindow)
                VideoMTC.IsFullWindow = false;
            else if (VideoMTC.IsCinema)
                VideoMTC.IsCinema = false;
            else if (VideoMTC.IsCompactOverlay)
                VideoMTC.IsCompactOverlay = false;
            else
                isHandled = false;
            return isHandled;
        }
        public void ShowTip(string content)
        {
            if (TipContainer.Visibility == Visibility.Collapsed)
                TipContainer.Visibility = Visibility.Visible;
            TipContentBlock.Text = content;
            _tipShowSeconds = 0;
        }
        public void HideTip()
        {
            if (TipContainer.Visibility == Visibility.Visible)
                TipContainer.Visibility = Visibility.Collapsed;
            TipContentBlock.Text = "";
            _tipShowSeconds = -1;
        }
        public void ChangeDanmakuBarDisplayMode(bool isSingleRow = false, bool isShow = true)
        {
            if (!isSingleRow)
            {
                bool needChange = Grid.GetRow(DanmakuBarContainer) == 1;
                if (needChange)
                {
                    Grid.SetRow(DanmakuBarContainer, 0);
                    if (isShow)
                        VisualStateManager.GoToState(mediaElement, "MarginState", false);
                }
            }
            else
            {
                bool needChange = Grid.GetRow(DanmakuBarContainer) == 0;
                if (needChange)
                {
                    Grid.SetRow(DanmakuBarContainer, 1);
                    VisualStateManager.GoToState(mediaElement, "DefaultState", false);
                }
            }
        }
        #endregion

    }
}
