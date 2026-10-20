using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Wdm.Ui.Controls;

/// <summary>
/// Modal dialog chrome: icon tile + title + close button, body (Content) and an optional footer.
/// Templated in Themes/Styles.xaml so every dialog shares the same look.
/// </summary>
public class DialogCard : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(DialogCard));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(DialogCard));

    public static readonly DependencyProperty FooterProperty =
        DependencyProperty.Register(nameof(Footer), typeof(object), typeof(DialogCard));

    public static readonly RoutedEvent CloseClickEvent =
        EventManager.RegisterRoutedEvent(nameof(CloseClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DialogCard));

    static DialogCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DialogCard), new FrameworkPropertyMetadata(typeof(DialogCard)));
    }

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    public event RoutedEventHandler CloseClick
    {
        add => AddHandler(CloseClickEvent, value);
        remove => RemoveHandler(CloseClickEvent, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Close") is Button close)
        {
            close.Click += (_, _) => RaiseEvent(new RoutedEventArgs(CloseClickEvent, this));
        }
    }
}
