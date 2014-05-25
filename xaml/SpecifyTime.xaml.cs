using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

namespace Seshat.xaml
{
    /// <summary>
    /// Interaction logic for SpecityTime.xaml
    /// </summary>
    public partial class SpecifyTime : Window
    {
        #region Fields

        private TimeSpan maxTimeSpan = TimeSpan.Zero;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the result.
        /// </summary>
        public double Result { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new specify time window.
        /// </summary>
        /// <param name="maxTime">The max time.</param>
        public SpecifyTime(TimeSpan maxTime)
        {
            this.maxTimeSpan = maxTime;
            InitializeComponent();

            this.hours.Items.Clear();
            for (int i = 0; i <= this.maxTimeSpan.Hours; i++)
                this.hours.Items.Add(i);

            this.minutes.IsEnabled = false;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Method fired when the user clicks on the ok button.
        /// </summary>
        private void okClicked(object sender, RoutedEventArgs e)
        {
            TimeSpan selectedTime = TimeSpan.Zero;
            selectedTime += TimeSpan.FromHours(this.hours.SelectedIndex);
            selectedTime += TimeSpan.FromMinutes(this.minutes.SelectedIndex);
            selectedTime += TimeSpan.FromSeconds(this.seconds.SelectedIndex);

            if (selectedTime >= TimeSpan.Zero && selectedTime <= maxTimeSpan)
            {
                this.DialogResult = true;
                this.Result = selectedTime.TotalSeconds;
                this.Close();
                return;
            }

            MessageBox.Show(this, "The selected time can't be outside of the max time.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// Method fired when the user selects a hour.
        /// </summary>
        private void hours_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.minutes != null)
            {
                this.minutes.Items.Clear();

                if (this.hours.SelectedIndex == this.hours.Items.Count - 1)
                    for (int i = 0; i <= this.maxTimeSpan.Minutes; i++)
                        this.minutes.Items.Add(i);
                else
                    for (int i = 0; i < 60; i++)
                        this.minutes.Items.Add(i);

                this.minutes.IsEnabled = true;
                this.seconds.IsEnabled = false;
                this.okBtn.IsEnabled = false;
            }
        }

        /// <summary>
        /// Method fired when the user selects a minute.
        /// </summary>
        private void minutes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.seconds != null)
            {
                this.seconds.Items.Clear();

                if (this.minutes.SelectedIndex == this.minutes.Items.Count - 1)
                    for (int i = 0; i <= this.maxTimeSpan.Seconds; i++)
                        this.seconds.Items.Add(i);
                else
                    for (int i = 0; i < 60; i++)
                        this.seconds.Items.Add(i);

                this.seconds.IsEnabled = true;
                this.okBtn.IsEnabled = false;
            }
        }

        /// <summary>
        /// Method fired when the user selects a second.
        /// </summary>
        private void seconds_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this.okBtn.IsEnabled = true;
        }

        #endregion
    }
}
