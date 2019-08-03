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
using System.Windows.Shapes;

using System.IO.Ports;
using LiveCharts;
using LiveCharts.Wpf;
using LiveCharts.Defaults;
using System.Collections.Concurrent;
using LiveCharts.Configurations;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.IO;
using System.Windows.Threading;
//using static Xceed.Wpf.Toolkit.DateTimePicker;

namespace HP53123_LongExperement
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //среднее между измерениями времени, сколько бегает время
        // проблема с перезапуском таймера..
        public SerialPort MainParam_SerialPort;
        public ChartValues<MeasureModel> MainChartValues { get; set; }
        public int MainParam_CounterMeasure;
        public int MainParam_CounterMeasure_Start;
        public int MainParam_CounterMeasure_End;
        StreamWriter MainParam_StreamWriter;
        DispatcherTimer MainParam_Timer;
        DateTime MainParam_TimeEndMeasure;
        public void System_ConnectToCOMPortFromAppSettings()
        {
            try
            {
                if (Properties.Settings.Default.COMName == "null")
                {
                    return;
                }
            }
            catch
            {
                return;
            }
            try
            {
                TextBlock_Status.Text = "Try connect to port " + Properties.Settings.Default.COMName;
                if (MainParam_SerialPort.IsOpen) MainParam_SerialPort.Close();

                MainParam_SerialPort.PortName = Properties.Settings.Default.COMName;
                MainParam_SerialPort.Handshake = Handshake.None;
                MainParam_SerialPort.Open();

                if (MainParam_SerialPort.IsOpen)
                {
                    TextBlock_Status.Text = "COM port is open (" + Properties.Settings.Default.COMName + ")";
                    Label_StatusCOM.Content = "Подключено " + Properties.Settings.Default.COMName + " " + Properties.Settings.Default.COMBaudrate.ToString() + "bps";
                    Label_StatusCOM.Background = new SolidColorBrush(Colors.LightGreen);
                    ComboBox_COMPorts.SelectedValue = Properties.Settings.Default.COMName;
                }
            }
            catch
            {
                TextBlock_Status.Text = "COM port connect error (" + Properties.Settings.Default.COMName + ")";
                Label_StatusCOM.Content = "Не подключено";
                Label_StatusCOM.Background = new SolidColorBrush(Colors.LightGray);
            }
        }

        public ConcurrentQueue<string> System_serialDataQueue = new ConcurrentQueue<string>();
        void System_SerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = sender as SerialPort;
            int bytesAvailable = sp.BytesToRead;
            byte[] recBuf = new byte[bytesAvailable];

            try
            {
                //sp.Read(recBuf, 0, bytesAvailable);
                string buffer = sp.ReadLine();
                System_serialDataQueue.Enqueue(buffer);
                //for (int index = 0; index < bytesAvailable; index++)
                //    System_serialDataQueue.Enqueue(recBuf[index]);
            }
            catch (TimeoutException ex)
            {
                MessageBox.Show(ex.ToString());
            }
            this.Dispatcher.Invoke(() => System_SerialDataProcessing());
            //this.Invoke((MethodInvoker)delegate
            //{
            //    this.System_SerialDataProcessing();
            //});
        }

        public void System_SerialDataProcessing()
        {
            try
            {
                string buffer;
                double value;
                while (System_serialDataQueue.TryDequeue(out buffer))
                {
                    double.TryParse(buffer.Replace(",", "").Replace(".", ",").Replace(" Hz\r", ""), out value);

                    MainChartValues.Add(new MeasureModel
                    {
                        Label = MainParam_CounterMeasure++,
                        Value = value
                    });

                    while (MainChartValues.Count > 150) MainChartValues.RemoveAt(0);

                    string temp = "";
                    if (Button_FileBurnStart.Content.ToString() == "ЗАПИСЬ...")
                    {
                        if ((bool)RadioButton_FixedPoint.IsChecked) temp += string.Format("{0:R}", value);
                        if ((bool)RadioButton_Scientific.IsChecked) temp += string.Format("{0:E}", value);
                        if ((bool)CheckBox_DateTime.IsChecked) temp += "\t" + string.Format("{0:u}", DateTime.Now).Replace("Z", "") + ":" + string.Format("{0:d}", DateTime.Now.Millisecond);
                        if ((bool)CheckBox_RAWColumn.IsChecked) temp += "\t" + buffer.Replace("\r", "");
                        MainParam_StreamWriter.WriteLine(temp);
                        if (MainParam_CounterMeasure % int.Parse(TextBox_FragmentSize.Text) == 0)
                        {
                            MainParam_StreamWriter.Flush();
                            
                            Label_StatusBurn.Content = "Записан фрагмент " + (MainParam_CounterMeasure / int.Parse(TextBox_FragmentSize.Text)).ToString() + "\nОкончание через " + (MainParam_TimeEndMeasure - DateTime.Now).ToString("dd'.'hh':'mm':'ss");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            Properties.Settings.Default.COMBaudrate = 19200;
            Properties.Settings.Default.Save();
            MainParam_SerialPort = new SerialPort(Properties.Settings.Default.COMName, Properties.Settings.Default.COMBaudrate, Parity.None, 8, StopBits.One);
            MainParam_SerialPort.DataReceived += new SerialDataReceivedEventHandler(System_SerialDataReceived);
            System_ConnectToCOMPortFromAppSettings();

            MainParam_CounterMeasure = 0;

            var mapper = Mappers.Xy<MeasureModel>()
               .X(model => model.Label)
               .Y(model => model.Value);
            Charting.For<MeasureModel>(mapper);

            DataContext = this;
            MainChartValues = new ChartValues<MeasureModel>();

            TextBox_FilePos.Text = "HP53132_Measure " + DateTime.Now.ToString("dd/MM/yyyy HH-mm-ss") + ".txt";

            DateTimePicker_deadline.Text = "asdsad";
        }

        private void ComboBox_COMPorts_DropDownOpened(object sender, EventArgs e)
        {
            if (MainParam_SerialPort.IsOpen)
            {
                MainParam_SerialPort.Close();
                Label_StatusCOM.Content = "Не подключено";
                Label_StatusCOM.Background = new SolidColorBrush(Colors.LightGray);
                TextBlock_Status.Text = "Disconect COM port";
            }
            
            try
            {
                string[] AviliblePorts;
                AviliblePorts = System.IO.Ports.SerialPort.GetPortNames();

                ComboBox_COMPorts.Items.Clear();
                foreach (string currentPort in AviliblePorts) ComboBox_COMPorts.Items.Add(currentPort);
                TextBlock_Status.Text = "Scan port complite";
            }
            catch
            {
                TextBlock_Status.Text = "Scan port error";
            }
        }

        private void ComboBox_COMPorts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboBox_COMPorts.SelectedItem.ToString().Length > 3)
            {
                try
                {
                    TextBlock_Status.Text = "Try connect to port " + ComboBox_COMPorts.SelectedItem.ToString();
                    Label_StatusCOM.Content = "Не подключено";
                    Label_StatusCOM.Background = new SolidColorBrush(Colors.LightGray);
                    if (MainParam_SerialPort.IsOpen) MainParam_SerialPort.Close();

                    MainParam_SerialPort.PortName = ComboBox_COMPorts.SelectedItem.ToString();
                    MainParam_SerialPort.Handshake = Handshake.None;
                    MainParam_SerialPort.Open();

                    if (MainParam_SerialPort.IsOpen)
                    {
                        TextBlock_Status.Text = "COM port is open (" + ComboBox_COMPorts.SelectedItem.ToString() + ")";
                        Label_StatusCOM.Content = "Подключено " + ComboBox_COMPorts.SelectedItem.ToString() + " " + Properties.Settings.Default.COMBaudrate.ToString() + "bps";
                        Label_StatusCOM.Background = new SolidColorBrush(Colors.LightGreen);
                        Properties.Settings.Default.COMName = ComboBox_COMPorts.SelectedItem.ToString();
                        Properties.Settings.Default.Save();
                    }
                }
                catch
                {
                    TextBlock_Status.Text = "COM port connect error (" + ComboBox_COMPorts.SelectedItem.ToString() + ")";
                    Label_StatusCOM.Content = "Не подключено";
                    Label_StatusCOM.Background = new SolidColorBrush(Colors.LightGray);
                }
            }
            else return;
        }

        private void Button_SetPathFile_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Text file (*.txt)|*.txt";
            saveFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            saveFileDialog.FileName = TextBox_FilePos.Text;
            if (saveFileDialog.ShowDialog() == true)
                TextBox_FilePos.Text = saveFileDialog.FileName;
        }

        private void Button_GenerateNewNameFile_Click(object sender, RoutedEventArgs e)
        {
            TextBox_FilePos.Text = "HP53132_Measure " + DateTime.Now.ToString("dd/MM/yyyy HH-mm-ss") + ".txt";
        }

        private void TextBox_FragmentSize_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void MainParam_Timer_Tick(object s,EventArgs e)
        {
            Button_FileBurnStart_Click(null, null);
        }

        private void Button_FileBurnStart_Click(object sender, RoutedEventArgs e)
        {
            if (Button_FileBurnStart.Content.ToString() == "ЗАПИСЬ...")
            {
                Button_FileBurnStart.Content = "Начать запись";
                Button_FileBurnStart.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(221, 221, 221));

                RadioButton_FixedPoint.IsEnabled = true;
                RadioButton_Scientific.IsEnabled = true;
                RadioButton_Ring.IsEnabled = true;
                RadioButton_Timer.IsEnabled = true;
                TextBox_FilePos.IsEnabled = true;
                Button_GenerateNewNameFile.IsEnabled = true;
                Button_SetPathFile.IsEnabled = true;
                CheckBox_RAWColumn.IsEnabled = true;
                CheckBox_DateTime.IsEnabled = true;

                MainParam_CounterMeasure_End = MainParam_CounterMeasure;
                MainChart.AxisX[0].Sections.Add(new AxisSection
                {
                    Value = MainParam_CounterMeasure_End,
                    StrokeThickness = 3,
                    Stroke = new SolidColorBrush(Color.FromRgb(220,30,30)),
                    DataLabel = true,
                });

                Label_StatusBurn.Content = "Запись окончена в " + DateTime.Now.ToString("HH:mm") + 
                    "\nЗаписано " + (MainParam_CounterMeasure_End - MainParam_CounterMeasure_Start).ToString() + " Значений";

                MainParam_StreamWriter.WriteLine("Окончание записи " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + 
                    ". Записано " + (MainParam_CounterMeasure_End - MainParam_CounterMeasure_Start).ToString() + " значений.");
                MainParam_StreamWriter.Flush();

                return;
            }
            if (Button_FileBurnStart.Content.ToString() == "Начать запись")
            {
                try
                {
                    string filepath;
                    if (TextBox_FilePos.Text.Contains("\\")) filepath = TextBox_FilePos.Text;
                    else filepath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\" + TextBox_FilePos.Text;
                    if (MainParam_StreamWriter != null) MainParam_StreamWriter.Close();
                    if (File.Exists(filepath))
                    {
                        if (MessageBox.Show("Файл " + filepath + " уже существует, дописать?", "Запись в файл", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            MainParam_StreamWriter = File.AppendText(filepath);
                        }
                        else return;
                    }
                    else
                    {
                        MainParam_StreamWriter = File.CreateText(filepath);
                    }
                    MainParam_StreamWriter.WriteLine("Старт записи измерений " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + " с частотомера HP53132A");
                    MainParam_StreamWriter.Flush();
                    MainParam_StreamWriter.AutoFlush = false;

                    Button_FileBurnStart.Content = "ЗАПИСЬ...";
                    Button_FileBurnStart.Background = new SolidColorBrush(Colors.OrangeRed);
                    RadioButton_FixedPoint.IsEnabled = false;
                    RadioButton_Scientific.IsEnabled = false;
                    RadioButton_Ring.IsEnabled = false;
                    RadioButton_Timer.IsEnabled = false;
                    TextBox_FilePos.IsEnabled = false;
                    Button_GenerateNewNameFile.IsEnabled = false;
                    Button_SetPathFile.IsEnabled = false;
                    CheckBox_RAWColumn.IsEnabled = false;
                    CheckBox_DateTime.IsEnabled = false;

                    MainParam_CounterMeasure_Start = MainParam_CounterMeasure;
                    MainChart.AxisX[0].Sections.Add(new AxisSection
                    {
                        Value = MainParam_CounterMeasure_Start,
                        StrokeThickness = 3,
                        Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 213, 72)),
                        DataLabel = true,
                    });

                    Label_StatusBurn.Content = "Запись начата в " + DateTime.Now.ToString("HH:mm");

                    if (MainParam_Timer != null) MainParam_Timer.Stop();
                     MainParam_Timer = new DispatcherTimer();

                    MainParam_Timer.Interval = TimeSpanUpDown_Timer.Value.Value;
                    MainParam_Timer.Start();
                    MainParam_Timer.Tick += MainParam_Timer_Tick;
                    MainParam_TimeEndMeasure = DateTime.Now.AddSeconds(TimeSpanUpDown_Timer.Value.Value.TotalSeconds);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
        }

        private void RadioButton_Timer_Checked(object sender, RoutedEventArgs e)
        {
            if (DateTimePicker_deadline != null)
            {
                if ((bool)RadioButton_Ring.IsChecked)
                {
                    DateTimePicker_deadline.IsEnabled = true;
                    TimeSpanUpDown_Timer.IsEnabled = false;
                }
                if ((bool)RadioButton_Timer.IsChecked)
                {
                    DateTimePicker_deadline.IsEnabled = false;
                    TimeSpanUpDown_Timer.IsEnabled = true;
                }
            }
        }

        private void MainChart_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            MainChart.AxisX[0].MinValue = double.NaN;
            MainChart.AxisX[0].MaxValue = double.NaN;
        }
    }
}
