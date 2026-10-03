using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CobraPix_PC.ViewModels;

namespace CobraPix_PC.Helpers
{
    /// <summary>
    /// Helper de navegação e confirmação amigável para campos de horário.
    /// Não interfere na digitação: permite digitação 100% natural e irrestrita (ex: 21:30, 21h30, 2130, 8:00, 8h00).
    /// Ao teclar Enter ou perder foco, aciona a confirmação e normalização do campo via ViewModel.
    /// </summary>
    public static class TimeMaskHelper
    {
        public static readonly DependencyProperty IsTimeMaskEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsTimeMaskEnabled",
                typeof(bool),
                typeof(TimeMaskHelper),
                new PropertyMetadata(false, OnIsTimeMaskEnabledChanged));

        public static bool GetIsTimeMaskEnabled(DependencyObject obj) =>
            (bool)obj.GetValue(IsTimeMaskEnabledProperty);

        public static void SetIsTimeMaskEnabled(DependencyObject obj, bool value) =>
            obj.SetValue(IsTimeMaskEnabledProperty, value);

        private static void OnIsTimeMaskEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox textBox) return;

            textBox.PreviewKeyDown -= TextBox_PreviewKeyDown;
            textBox.LostFocus -= TextBox_LostFocus;

            if ((bool)e.NewValue)
            {
                textBox.PreviewKeyDown += TextBox_PreviewKeyDown;
                textBox.LostFocus += TextBox_LostFocus;
            }
        }

        private static void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox)
            {
                var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private static void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            var binding = textBox.GetBindingExpression(TextBox.TextProperty);
            binding?.UpdateSource();

            if (textBox.DataContext is ConfiguracoesViewModel vm)
            {
                vm.OnHourSlotLostFocus();
            }
        }
    }
}
