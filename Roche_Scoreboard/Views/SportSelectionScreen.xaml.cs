using System;
using System.Windows;
using System.Windows.Input;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Views
{
    public partial class SportSelectionScreen : System.Windows.Controls.UserControl
    {
        public event Action<SportMode>? SportSelected;

        public SportSelectionScreen()
        {
            InitializeComponent();
        }

        private void AFL_Click(object sender, MouseButtonEventArgs e)
        {
            SportSelected?.Invoke(SportMode.AFL);
        }

        private void Cricket_Click(object sender, MouseButtonEventArgs e)
        {
            SportSelected?.Invoke(SportMode.Cricket);
        }
    }
}
