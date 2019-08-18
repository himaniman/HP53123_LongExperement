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

//using Device.Net;
//using Usb.Net.Windows;
//using Windows.Devices.HumanInterfaceDevice;

using HidSharp.Utility;
using HidSharp;
using System.Threading;


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
        public Func<double, string> YFormatter { get; set; }
        public int MainParam_CounterMeasure;
        public int MainParam_CounterMeasure_Start;
        public int MainParam_CounterMeasure_End;
        StreamWriter MainParam_StreamWriter;
        DispatcherTimer MainParam_Timer;
        DateTime MainParam_TimeEndMeasure;
        DateTime MainParam_TimeStartMeasure;
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
                double value = 0;
                while (System_serialDataQueue.TryDequeue(out buffer))
                {
                    if (buffer.Contains("kHz"))
                    {
                        double.TryParse(buffer.Replace(",", "").Replace(".", ",").Replace(" kHz\r", ""), out value);
                        value *= 1000L;
                    }
                    else if (buffer.Contains("MHz"))
                    {
                        double.TryParse(buffer.Replace(",", "").Replace(".", ",").Replace(" MHz\r", ""), out value);
                        value *= 1000000L;
                    }
                    else if (buffer.Contains("GHz"))
                    {
                        double.TryParse(buffer.Replace(",", "").Replace(".", ",").Replace(" GHz\r", ""), out value);
                        value *= 1000000000L;
                    }
                    else if (buffer.Contains("Hz"))
                    {
                        double.TryParse(buffer.Replace(",", "").Replace(".", ",").Replace(" Hz\r", ""), out value);
                    }


                    MainChartValues.Add(new MeasureModel
                    {
                        Label = MainParam_CounterMeasure++,
                        Value = value
                    });

                    if ((bool)RadioButton_FixedPoint.IsChecked) TextBlock_CurrentValue.Text = value.ToString("N") + " Hz";
                    if ((bool)RadioButton_Scientific.IsChecked) TextBlock_CurrentValue.Text = value.ToString("e") + " Hz";

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
                            if (MainParam_CounterMeasure_End == 0)
                            {
                                Label_StatusBurn.Content = "Записано значений " + (MainParam_CounterMeasure - MainParam_CounterMeasure_Start).ToString() + "\nОкончание через " + (MainParam_TimeEndMeasure - DateTime.Now).ToString("dd'.'hh':'mm':'ss");
                            }
                            else
                            {
                                Label_StatusBurn.Content = "Записано значений " + (MainParam_CounterMeasure - MainParam_CounterMeasure_Start).ToString() + "\nОкончание через " + (MainParam_CounterMeasure_End - MainParam_CounterMeasure).ToString();
                            }
                        }
                        if (MainParam_CounterMeasure_End == 0)
                        {
                            ProgressBar_Status.Value = ((double)(DateTime.Now.Ticks - MainParam_TimeStartMeasure.Ticks) / (double)(MainParam_TimeEndMeasure.Ticks - MainParam_TimeStartMeasure.Ticks)) * 100;
                            TextBlock_ETA.Text = "ETA: " + (MainParam_TimeEndMeasure - DateTime.Now).ToString("dd'.'hh':'mm':'ss");
                        }
                        if (MainParam_CounterMeasure_End > 0)
                        {
                            ProgressBar_Status.Value = ((double)(MainParam_CounterMeasure - MainParam_CounterMeasure_Start) / (double)(MainParam_CounterMeasure_End - MainParam_CounterMeasure_Start)) * 100;
                            TextBlock_ETA.Text = "ETA: " + (MainParam_CounterMeasure_End - MainParam_CounterMeasure).ToString();
                            if (MainParam_CounterMeasure_End == MainParam_CounterMeasure)
                            {
                                Button_FileBurnStart_Click(null, null);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        HidDevice device;
        HidStream stream;
        byte[] buff = new byte[20];

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

            YFormatter = value => value.ToString("e");


            //Application.Current.Dispatcher.Invoke(myFunc);

            HidSharpDiagnostics.EnableTracing = true;
            HidSharpDiagnostics.PerformStrictChecks = true;
            var list = DeviceList.Local;
            var deviceList = list.GetDevices(DeviceTypes.Hid).ToArray();

            //foreach (Device dev in allDeviceList)
            //{
            //    //MessageBox.Show(dev.ToString() + " @ " + dev.DevicePath);
            //}

            //HidDeviceLoader loader = new HidDeviceLoader();
            //var deviceList2 = loader.GetDevices().ToArray();
            String a = "";
            foreach (HidDevice dev in deviceList)
            {
                a += dev + "\n";
            }
            MessageBox.Show(a);
            
            list.TryGetHidDevice(out device, vendorID: 6790, productID: 57352);
            if (device == null) MessageBox.Show("error open");
            
            if (!device.TryOpen(out stream)) MessageBox.Show("error open stream");
            //AsyncCallback callBack = new AsyncCallback(myFunc);
            //IAsyncResult callBack2 = new IAsyncResult(myFunc2);

            byte[] jjj = new byte []{ 0x00, 0x4B, 0x00, 0x00, 0x00 };
            try
            {
                //device.GetRawReportDescriptor();
                stream.GetFeature(jjj);
                //stream.SetFeature(jjj);
            }
            catch
            {

            }
            

            stream.BeginRead(buff, 0, 20, new AsyncCallback(myFunc), null);
            //stream.EndRead(myFunc2);
            //while (true)
            //{
            //    var bytes = new byte[device.MaxInputReportLength];
            //    int count;

            //    try
            //    {
            //        count = stream.Read(bytes, 0, bytes.Length);

            //    }
            //    catch (TimeoutException)
            //    {
            //        MessageBox.Show("Read timed out.");
            //        continue;
            //    }
            //}


        }

        void myFunc(IAsyncResult result)
        {
            int bytesRead = 0;
            try
            {
                bytesRead = stream.EndRead(result);
            }
            catch
            {

            }

            //MessageBox.Show(buff.Length.ToString());
            int a = 0;
            if (buff[1] == 0xF1) a=((int)buff[2]) + (int)buff[3];

            {
                Dispatcher.Invoke(
                    new Action(() =>
                    {
                        if (a!=0)
                        TextBlock_CurrentValue.Text = BitConverter.ToString(buff) + "\n"+ a.ToString();
                    }
                    ));
            }

            //MessageBox.Show(buff.Length.ToString());
            Thread.Sleep(100);

            buff = new byte[20];
            stream.BeginRead(buff, 0, 20, new AsyncCallback(myFunc), null);
        }

        public void myFunc2(IAsyncResult res)
        {

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
                RadioButton_QtyMeas.IsEnabled = true;
                RadioButton_Timer.IsEnabled = true;
                TextBox_FilePos.IsEnabled = true;
                Button_GenerateNewNameFile.IsEnabled = true;
                Button_SetPathFile.IsEnabled = true;
                CheckBox_RAWColumn.IsEnabled = true;
                CheckBox_DateTime.IsEnabled = true;
                TextBox_FragmentSize.IsEnabled = true;

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

                ProgressBar_Status.Value = 0;
                TextBlock_ETA.Text = "";
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
                    RadioButton_QtyMeas.IsEnabled = false;
                    RadioButton_Timer.IsEnabled = false;
                    TextBox_FilePos.IsEnabled = false;
                    Button_GenerateNewNameFile.IsEnabled = false;
                    Button_SetPathFile.IsEnabled = false;
                    CheckBox_RAWColumn.IsEnabled = false;
                    CheckBox_DateTime.IsEnabled = false;
                    TextBox_FragmentSize.IsEnabled = false;

                    MainParam_CounterMeasure_Start = MainParam_CounterMeasure;
                    MainParam_TimeStartMeasure = DateTime.Now;
                    MainChart.AxisX[0].Sections.Add(new AxisSection
                    {
                        Value = MainParam_CounterMeasure_Start,
                        StrokeThickness = 3,
                        Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 213, 72)),
                        DataLabel = true,
                    });

                    Label_StatusBurn.Content = "Запись начата в " + DateTime.Now.ToString("HH:mm");

                    if ((bool)RadioButton_Timer.IsChecked)
                    {
                        MainParam_CounterMeasure_End = 0;
                        if (MainParam_Timer != null) MainParam_Timer.Stop();
                        MainParam_Timer = new DispatcherTimer();

                        MainParam_Timer.Interval = TimeSpanUpDown_Timer.Value.Value;
                        MainParam_Timer.Start();
                        MainParam_Timer.Tick += MainParam_Timer_Tick;
                        MainParam_TimeEndMeasure = DateTime.Now.AddSeconds(TimeSpanUpDown_Timer.Value.Value.TotalSeconds);
                    }
                    if ((bool)RadioButton_QtyMeas.IsChecked)
                    {
                        MainParam_CounterMeasure_End = MainParam_CounterMeasure + int.Parse(TextBox_QtyMeas.Text);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
        }

        private void RadioButton_Timer_Checked(object sender, RoutedEventArgs e)
        {
            if (TextBox_QtyMeas != null)
            {
                if ((bool)RadioButton_QtyMeas.IsChecked)
                {
                    TextBox_QtyMeas.IsEnabled = true;
                    TimeSpanUpDown_Timer.IsEnabled = false;
                }
                if ((bool)RadioButton_Timer.IsChecked)
                {
                    TextBox_QtyMeas.IsEnabled = false;
                    TimeSpanUpDown_Timer.IsEnabled = true;
                }
            }
        }

        private void RadioButton_QtyMeas_Checked(object sender, RoutedEventArgs e)
        {
            if ((bool)RadioButton_QtyMeas.IsChecked)
            {
                TextBox_QtyMeas.IsEnabled = true;
                TimeSpanUpDown_Timer.IsEnabled = false;
            }
            if ((bool)RadioButton_Timer.IsChecked)
            {
                TextBox_QtyMeas.IsEnabled = false;
                TimeSpanUpDown_Timer.IsEnabled = true;
            }
        }

        private void MainChart_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            MainChart.AxisX[0].MinValue = double.NaN;
            MainChart.AxisX[0].MaxValue = double.NaN;
        }

        private void TextBox_QtyMeas_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {

        }

        private void RadioButton_Scientific_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)RadioButton_Scientific.IsChecked) YFormatter = value => value.ToString("e");
            if ((bool)RadioButton_FixedPoint.IsChecked) YFormatter = value => value.ToString("N") + " Hz";
            MainChart.AxisY[0].LabelFormatter = YFormatter;
            //Random x = new Random();
            //System_serialDataQueue.Enqueue("40,"+ x.Next(100,999) + ".100 Hz\r\n");
            //System_SerialDataProcessing();
        }
    }
}
