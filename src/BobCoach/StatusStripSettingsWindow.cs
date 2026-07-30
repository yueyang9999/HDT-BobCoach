using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BobCoach
{
    internal sealed class StatusStripSettingsWindow : Window
    {
        private readonly OverlayRenderer _renderer;
        private readonly CheckBox _unlockCheckBox;
        private readonly Slider _fontSizeSlider;
        private readonly TextBlock _fontSizeValue;
        private bool _syncing;

        public StatusStripSettingsWindow(OverlayRenderer renderer)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            _renderer = renderer;

            Title = "Bob教练设置";
            Width = 360;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _unlockCheckBox = new CheckBox
            {
                Content = "解锁状态面板",
                IsChecked = _renderer.StatusStripUnlocked,
                Margin = new Thickness(0, 0, 0, 18),
            };
            _unlockCheckBox.Checked += UnlockCheckBoxChanged;
            _unlockCheckBox.Unchecked += UnlockCheckBoxChanged;
            Grid.SetRow(_unlockCheckBox, 0);
            root.Children.Add(_unlockCheckBox);

            var fontGrid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            var fontLabel = new TextBlock
            {
                Text = "字体大小",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            Grid.SetColumn(fontLabel, 0);
            fontGrid.Children.Add(fontLabel);

            _fontSizeSlider = new Slider
            {
                Minimum = Engine.StatusStripLayoutPolicy.MinimumFontSize,
                Maximum = Engine.StatusStripLayoutPolicy.MaximumFontSize,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Value = _renderer.StatusStripFontSize,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _fontSizeSlider.ValueChanged += FontSizeSliderValueChanged;
            _fontSizeSlider.PreviewMouseLeftButtonUp += FontSizeSliderMouseLeftButtonUp;
            _fontSizeSlider.PreviewKeyUp += FontSizeSliderKeyUp;
            Grid.SetColumn(_fontSizeSlider, 1);
            fontGrid.Children.Add(_fontSizeSlider);

            _fontSizeValue = new TextBlock
            {
                Text = ((int)_renderer.StatusStripFontSize).ToString(),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(_fontSizeValue, 2);
            fontGrid.Children.Add(_fontSizeValue);
            Grid.SetRow(fontGrid, 1);
            root.Children.Add(fontGrid);

            var resetButton = new Button
            {
                Content = "恢复默认",
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 88,
                Padding = new Thickness(12, 5, 12, 5),
            };
            resetButton.Click += ResetButtonClick;
            Grid.SetRow(resetButton, 2);
            root.Children.Add(resetButton);

            Content = root;
            _renderer.StatusStripUnlockedChanged += RendererStatusStripUnlockedChanged;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _renderer.SetStatusStripUnlocked(false);
            _renderer.SaveStatusStripSettings();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _renderer.StatusStripUnlockedChanged -= RendererStatusStripUnlockedChanged;
            base.OnClosed(e);
        }

        private void UnlockCheckBoxChanged(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            _renderer.SetStatusStripUnlocked(_unlockCheckBox.IsChecked == true);
        }

        private void FontSizeSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;
            int value = Engine.StatusStripLayoutPolicy.NormalizeFontSize(e.NewValue);
            _fontSizeValue.Text = value.ToString();
            _renderer.SetStatusStripFontSize(value, false);
        }

        private void FontSizeSliderMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _renderer.SaveStatusStripSettings();
        }

        private void FontSizeSliderKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down
                || e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown)
                _renderer.SaveStatusStripSettings();
        }

        private void ResetButtonClick(object sender, RoutedEventArgs e)
        {
            _renderer.ResetStatusStripCustomization();
            _syncing = true;
            _fontSizeSlider.Value = _renderer.StatusStripFontSize;
            _fontSizeValue.Text = ((int)_renderer.StatusStripFontSize).ToString();
            _syncing = false;
        }

        private void RendererStatusStripUnlockedChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => RendererStatusStripUnlockedChanged(sender, e)));
                return;
            }

            _syncing = true;
            _unlockCheckBox.IsChecked = _renderer.StatusStripUnlocked;
            _syncing = false;
        }
    }
}
