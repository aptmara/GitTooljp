using System.Windows;
using System.Windows.Controls;

namespace SimplePRClient.Helpers
{
    public static class BrowserBehavior
    {
        public static readonly DependencyProperty HtmlProperty = DependencyProperty.RegisterAttached(
            "Html",
            typeof(string),
            typeof(BrowserBehavior),
            new FrameworkPropertyMetadata(OnHtmlChanged));

        [AttachedPropertyBrowsableForType(typeof(WebBrowser))]
        public static string GetHtml(DependencyObject d)
        {
            return (string)d.GetValue(HtmlProperty);
        }

        public static void SetHtml(DependencyObject d, string value)
        {
            d.SetValue(HtmlProperty, value);
        }

        static void OnHtmlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebBrowser wb)
            {
                 if (e.NewValue is string html)
                 {
                     try
                     {
                         wb.NavigateToString(html);
                     }
                     catch
                     {
                         // Ignore
                     }
                 }
                 else
                 {
                     wb.NavigateToString("<html></html>");
                 }
            }
        }
    }
}
