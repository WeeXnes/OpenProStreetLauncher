using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenProStreetLauncher
{
    // Small builders for the window's controls, styled by App.axaml.
    static class Ui
    {
        public static readonly IBrush Success = new SolidColorBrush(Color.Parse("#4ADE80"));
        public static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#FBBF24"));
        public static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#F87171"));
        public static readonly IBrush Idle = new SolidColorBrush(Color.Parse("#808080"));
        public static readonly IBrush Line = new SolidColorBrush(Color.Parse("#1F1F1F"));

        public static TextBlock Text(string text, string cls = "body")
        {
            TextBlock t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            foreach (string c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)) t.Classes.Add(c);
            return t;
        }

        public static TextBlock Micro(string text) => Text(text.ToUpperInvariant(), "micro");

        public static Button Button(string text, bool primary = false, Action click = null)
        {
            Button b = new Button { Content = text };
            if (primary) b.Classes.Add("primary");
            if (click != null) b.Click += (s, e) => click();
            return b;
        }

        public static StackPanel Stack(double spacing, params Control[] children)
        {
            StackPanel p = new StackPanel { Spacing = spacing };
            foreach (Control c in children) p.Children.Add(c);
            return p;
        }
    }

    // One line in a list: a status dot, a name, what's there, and a button to fix it.
    class StatusRow : Border
    {
        readonly Ellipse m_dot = new Ellipse { Width = 8, Height = 8, Fill = Ui.Idle, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock m_title;
        readonly TextBlock m_detail = Ui.Text("Checking...", "small");
        readonly Button m_action = new Button { IsVisible = false, MinWidth = 104 };
        Func<Task> m_onClick;

        public StatusRow(string title, bool last = false)
        {
            m_title = Ui.Text(title, "strong");
            m_action.Click += async (s, e) => { if (m_onClick != null) await m_onClick(); };
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = 76 };
            m_dot.Margin = new Thickness(22, 0, 16, 0);
            g.Children.Add(m_dot);
            StackPanel text = Ui.Stack(4, m_title, m_detail);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(0, 12, 16, 12);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            m_action.Margin = new Thickness(0, 0, 20, 0);
            m_action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(m_action, 2);
            g.Children.Add(m_action);
            Child = g;
            BorderBrush = Ui.Line;
            BorderThickness = last ? new Thickness(0) : new Thickness(0, 0, 0, 1);
        }

        public void Set(IBrush dot, string detail, string action = null, Func<Task> onClick = null)
        {
            m_dot.Fill = dot;
            m_detail.Text = detail;
            m_onClick = onClick;
            m_action.IsVisible = action != null;
            if (action != null) m_action.Content = action;
        }

        public bool ActionEnabled { set => m_action.IsEnabled = value; }
    }

    // A message or a yes/no question, in the window's style.
    static class Dialog
    {
        public static Task Show(Window owner, string title, string text) => Ask(owner, title, text, "OK", null);

        public static async Task<bool> Ask(Window owner, string title, string text, string yes, string no)
        {
            bool answer = false;
            Window w = new Window
            {
                Title = title,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
            if (no != null) buttons.Children.Add(Ui.Button(no, false, () => w.Close()));
            buttons.Children.Add(Ui.Button(yes, true, () => { answer = true; w.Close(); }));
            w.Content = new Border
            {
                Padding = new Thickness(28),
                Child = Ui.Stack(16, Ui.Text(title, "strong"), Ui.Text(text), buttons),
            };
            await w.ShowDialog(owner);
            return answer;
        }
    }
}
