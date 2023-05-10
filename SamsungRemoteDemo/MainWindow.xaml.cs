using SamsungRemoteLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SamsungRemoteDemo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SamsungRemote sr;
        public MainWindow()
        {
            InitializeComponent();

            Settings s = new Settings("CustomAppName", "***REMOVED***", "***REMOVED***");
            sr = new SamsungRemote(s);

            if (!sr.IsActive()) // Sends HTTP GET request to check TV availability, WARNING: returns true for ~30 seconds after power off)
            {
                sr.TurnOn(); // Sends Wake On Lan Magic Packet, can use repeat parameter  to send multiple times)
            }
            
            /*
            bool res = await sr.IsActiveAsync();
            if (!res) await Task.Run(() => { sr.TurnOn() });
            */

            if (sr.IsActive(1000)) // Can use delay parameter to specify wait time before sending request
            {

            }
        }
    }
}
