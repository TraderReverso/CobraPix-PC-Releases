using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CobraPix_PC.Models;

namespace CobraPix_PC.Views
{
    /// <summary>
    /// Janela popup flutuante de notificação no estilo nativo do Windows, posicionada no canto inferior direito.
    /// </summary>
    public partial class NotificationToastWindow : Window
    {
        private static readonly List<NotificationToastWindow> OpenToasts = new();
        private readonly DispatcherTimer _autoCloseTimer;

        public NotificationToastWindow(NotificationItem item)
        {
            InitializeComponent();

            TxtIcon.Text = item.Icon;
            TxtBadge.Text = item.TypeBadge;
            TxtTitle.Text = item.Title;
            TxtMessage.Text = item.Message;

            _autoCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(6)
            };
            _autoCloseTimer.Tick += (s, e) =>
            {
                _autoCloseTimer.Stop();
                CloseToast();
            };

            Loaded += (s, e) =>
            {
                PositionOnScreen();
                _autoCloseTimer.Start();
            };
        }

        public static void ShowNotification(NotificationItem item)
        {
            try
            {
                var toast = new NotificationToastWindow(item);
                OpenToasts.Add(toast);
                toast.Closed += (s, e) => OpenToasts.Remove(toast);
                toast.Show();
            }
            catch
            {
                // Silencioso em caso de impossibilidade gráfica
            }
        }

        private void PositionOnScreen()
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                int index = OpenToasts.IndexOf(this);
                if (index < 0) index = 0;

                double offset = index * (ActualHeight > 0 ? ActualHeight + 10 : 120);

                Left = workArea.Right - Width - 16;
                Top = workArea.Bottom - (ActualHeight > 0 ? ActualHeight : 110) - 16 - offset;
            }
            catch
            {
                // Fallback padrão
                Left = 100;
                Top = 100;
            }
        }

        private void OnCardClicked(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (Application.Current?.MainWindow != null)
                {
                    var mainWin = Application.Current.MainWindow;
                    if (mainWin.WindowState == WindowState.Minimized)
                    {
                        mainWin.WindowState = WindowState.Normal;
                    }
                    mainWin.Activate();
                    mainWin.Focus();
                }
            }
            catch
            {
            }
            finally
            {
                CloseToast();
            }
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            CloseToast();
        }

        private void CloseToast()
        {
            _autoCloseTimer.Stop();
            try
            {
                Close();
            }
            catch
            {
            }
        }
    }
}
