using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CobraPix_PC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Quando iniciado automaticamente pelo Windows (argumento --minimized),
            // abre a janela já no estado minimizado sem interromper o usuário.
            if (App.StartMinimized)
            {
                WindowState = WindowState.Minimized;
            }
        }
    }
}