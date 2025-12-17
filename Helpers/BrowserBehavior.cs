namespace SimplePRClient.Helpers;

using System.Windows;
using System.Windows.Controls;

public static class BrowserBehavior
{
    public static readonly DependencyProperty HtmlProperty = DependencyProperty.RegisterAttached(
        "Html",
        typeof(string),
        typeof(BrowserBehavior),
        new FrameworkPropertyMetadata(OnHtmlChanged));

    [AttachedPropertyBrowsableForType(typeof(WebBrowser))]
    public static string GetHtml(WebBrowser d)
    {
        return (string)d.GetValue(HtmlProperty);
    }

    public static void SetHtml(WebBrowser d, string value)
    {
        d.SetValue(HtmlProperty, value);
    }

    private static void OnHtmlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WebBrowser wb)
        {
            var html = e.NewValue as string ?? "";
            if (string.IsNullOrEmpty(html))
            {
                wb.NavigateToString("<html><body></body></html>");
            }
            else
            {
                wb.NavigateToString(html);
            }
        }
    }
}
