using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

internal class TabSelectionHarness : Window
{
    private readonly TabControl MainTabControl;
    private readonly TabItem StatsTabItem;
    private readonly ScrollViewer StatsScrollViewer;
    public TabSelectionHarness(Window view)
    {
        MainTabControl = (TabControl)view.FindName("MainTabControl");
        StatsTabItem = (TabItem)view.FindName("StatsTabItem");
        StatsScrollViewer = (ScrollViewer)view.FindName("StatsScrollViewer");
        MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;
    }
        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, MainTabControl)) return;
            if (e.AddedItems.Count == 0) return;
            if (e.AddedItems[0] is not TabItem tabItem) return;
            if (tabItem.Content is not UIElement content) return;

            if (ReferenceEquals(tabItem, StatsTabItem))
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (ReferenceEquals(MainTabControl.SelectedItem, StatsTabItem))
                        StatsScrollViewer.ScrollToTop();
                }));

            content.Opacity = 0;
            var translate = new TranslateTransform(0, 8);
            content.RenderTransform = translate;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideUp = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            content.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            translate.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }


}
