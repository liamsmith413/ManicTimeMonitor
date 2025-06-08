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
using System.Windows.Navigation;
using System.Diagnostics;
using System.Timers;
using System.IO;
using System.Windows.Threading;
using System.Globalization;
using System.Data.SQLite;

namespace ManicTimeMonitor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly string manicTimePath = @"C:\Program Files (x86)\ManicTime\ManicTime.exe";
        private Timer monitorTimer;
        private DispatcherTimer resourceTimer;
        private PerformanceCounter cpuCounter;
        private PerformanceCounter cpuCounterTotal;
        private PerformanceCounter ramCounter;
        private Process currentProcess;
        private string dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Finkit\ManicTime\ManicTimeReports.db");

        public MainWindow()
        {
            InitializeComponent(); // Ensure MainWindow.xaml exists and is properly linked to this class
            StartMonitoring();
            StartResourceMonitoring();
            StartActiveTimeMonitoring(); // Add this line
        }

        private void StartMonitoring()
        {
            monitorTimer = new Timer
            {
                Interval = 2000, // Check every 2 seconds
                AutoReset = true
            };
            monitorTimer.Elapsed += MonitorTimer_Elapsed;
            monitorTimer.Start();
        }

        private void MonitorTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            bool isRunning = Process.GetProcessesByName("ManicTime").Any();
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = isRunning ? "\U0001f7e2 ManicTime" : "🔴 ManicTime";
                StatusTextBlock.Foreground = isRunning ? Brushes.LightGreen : Brushes.OrangeRed;
            });

            if (!isRunning)
            {
                NotifyUser();
                RestartManicTime();
            }
        }

        private void NotifyUser()
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show("ManicTime has stopped. Attempting to restart.", "ManicTime Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        private void RestartManicTime()
        {
            if (File.Exists(manicTimePath))
            {
                Process.Start(manicTimePath);
            }
        }

        // --- Resource Monitoring Logic Below ---

        private void StartResourceMonitoring()
        {
            try
            {
                // Initialize system-wide performance counters
                cpuCounterTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");

                resourceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                resourceTimer.Tick += ResourceTimer_Tick;
                resourceTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing performance counters: {ex.Message}");
            }
        }

        private void ResourceTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Get CPU usage
                float cpuUsage = cpuCounterTotal.NextValue();

                // Get RAM usage
                float ramPercentage = ramCounter.NextValue();
                ulong totalMemoryKB = new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / 1024;
                ulong usedMemoryKB = (ulong)(totalMemoryKB * (ramPercentage / 100.0));
                double usedMemoryGB = Math.Round(usedMemoryKB / (1024.0 * 1024.0), 1);

                Dispatcher.Invoke(() =>
                {
                    // Update CPU display with color coding
                    CpuUsageTextBlock.Text = $"{cpuUsage:F1}%";
                    CpuUsageTextBlock.Foreground = new SolidColorBrush(GetResourceColor(cpuUsage));

                    // Update RAM display with color coding
                    RamUsageTextBlock.Text = $"{usedMemoryGB:F1}GB";
                    RamUsageTextBlock.Foreground = new SolidColorBrush(GetResourceColor(ramPercentage));
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = $"Error: {ex.Message}";
                    StatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                });
            }
        }

        private Color GetResourceColor(double percentage)
        {
            if (percentage >= 90)
                return (Color)ColorConverter.ConvertFromString("#EF4444"); // Red
            if (percentage >= 70)
                return (Color)ColorConverter.ConvertFromString("#F59E0B"); // Orange
            if (percentage >= 50)
                return (Color)ColorConverter.ConvertFromString("#10B981"); // Green
            return (Color)ColorConverter.ConvertFromString("#E5E5E5"); // Normal
        }

        private void StartActiveTimeMonitoring()
        {
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1) // Update every minute
            };
            timer.Tick += ActiveTimeTimer_Tick;
            timer.Start();

            // Initial update
            UpdateActiveTime();
        }

        private void ActiveTimeTimer_Tick(object sender, EventArgs e)
        {
            UpdateActiveTime();
        }

        private void UpdateActiveTime()
        {
            try
            {
                using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand(connection))
                    {
                        command.CommandText = @"
                            SELECT 
                                SUM(
                                    JULIANDAY(EndLocalTime) - JULIANDAY(StartLocalTime)
                                ) * 24 * 60 AS TotalActiveMinutes
                            FROM Ar_Activity 
                            WHERE 
                                DATE(StartLocalTime) = DATE('now', 'localtime') AND
                                ReportId = 2 AND
                                IsActive = 1;";

                        var result = command.ExecuteScalar();
                        if (result != DBNull.Value && result != null)
                        {
                            double minutes = Convert.ToDouble(result);
                            int hours = (int)(minutes / 60);
                            int remainingMinutes = (int)(minutes % 60);

                            Dispatcher.Invoke(() =>
                            {
                                ActiveTimeTextBlock.Text = $"{hours}h {remainingMinutes}m";
                                ActiveTimeTextBlock.Foreground = new SolidColorBrush(
                                    (Color)ColorConverter.ConvertFromString("#10B981"));
                            });
                        }
                    }
                }
            }
            catch (Exception)
            {
                Dispatcher.Invoke(() =>
                {
                    ActiveTimeTextBlock.Text = "Error";
                    ActiveTimeTextBlock.Foreground = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString("#EF4444"));
                });
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            ContextMenu.IsOpen = true;
        }

        private void Opacity_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string opacity)
            {
                if (double.TryParse(opacity, out double value))
                {
                    this.Opacity = value;
                }
            }
        }

        private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                this.Topmost = menuItem.IsChecked;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Make sure window starts on top and with full opacity
            this.Topmost = true;
            this.Opacity = 1.0;
            this.ShowInTaskbar = false;

            // Get the current process to monitor
            try
            {
                var manicTimeProcesses = Process.GetProcessesByName("ManicTime");
                if (manicTimeProcesses.Any())
                {
                    currentProcess = manicTimeProcesses.First();
                    cpuCounter = new PerformanceCounter("Process", "% Processor Time",
                        GetProcessInstanceName(currentProcess), true);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error: {ex.Message}";
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            cpuCounterTotal?.Dispose();
            ramCounter?.Dispose();
            cpuCounter?.Dispose();
            base.OnClosing(e);
        }
        private string GetProcessInstanceName(Process process)
        {
            var category = new PerformanceCounterCategory("Process");
            var instances = category.GetInstanceNames();

            foreach (var instance in instances)
            {
                using (var counter = new PerformanceCounter("Process", "ID Process", instance, true))
                {
                    if ((int)counter.RawValue == process.Id)
                    {
                        return instance;
                    }
                }
            }

            throw new InvalidOperationException("Could not find performance counter instance name for process.");
        }
    }

    public class GreaterThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && parameter != null)
            {
                double doubleValue = System.Convert.ToDouble(value);
                double compareValue = System.Convert.ToDouble(parameter);
                return doubleValue > compareValue;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
