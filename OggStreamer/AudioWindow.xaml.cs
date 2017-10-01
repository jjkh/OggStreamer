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

using CSCore;
using CSCore.Codecs.WAV;
using CSCore.Streams;
using CSCore.Win32;
using CSCore.SoundIn;
using CSCore.CoreAudioAPI;
using System.IO;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;

namespace OggStreamer
{
    /// <summary>
    /// Interaction logic for AudioWindow.xaml
    /// </summary>
    public partial class AudioWindow : Window
    {
        bool _looping = false;
        private WasapiCapture _soundIn;
        private IWaveSource _finalSource;
        private Process _oggEncProcess;
        private AsyncStreamChunker _stdOut;
        private AsyncTcpListener _tcpListener;

        public AudioWindow()
        {
            InitializeComponent();
        }

        private void LoopbackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_looping)
            {
                StopAudioLoopback();

                LoopbackButton.Content = "Loopback Audio";
                _looping = false;
            }
            else
            {
                StartAudioLoopback();

                LoopbackButton.Content = "Stop Loopback";
                _looping = true;
            }
        }

        private void StopAudioLoopback()
        {
            _soundIn.Stop();
            _soundIn.Dispose();
            _finalSource.Dispose();
            _stdOut.Stop();
            _oggEncProcess.StandardInput.Close();
            _oggEncProcess.StandardOutput.Close();
            _oggEncProcess.WaitForExit();
            _oggEncProcess.Dispose();
        }

        private void StartAudioLoopback()
        {
            _oggEncProcess = new Process();
            _oggEncProcess.StartInfo.UseShellExecute = false;
            _oggEncProcess.StartInfo.RedirectStandardInput = true;
            _oggEncProcess.StartInfo.RedirectStandardOutput = true;
            _oggEncProcess.StartInfo.FileName = "oggenc2.exe";
            _oggEncProcess.StartInfo.Arguments = "--raw --raw-format=3 --raw-rate=48000 --resample 44100 -";
            _oggEncProcess.StartInfo.CreateNoWindow = true;
            _oggEncProcess.Start();

            _soundIn = new WasapiLoopbackCapture();
            _soundIn.Initialize();
            var soundInSource = new SoundInSource(_soundIn);
            var singleBlockNotificationStream = new SingleBlockNotificationStream(soundInSource.ToSampleSource());
            _finalSource = singleBlockNotificationStream.ToWaveSource();

            byte[] inBuffer = new byte[_finalSource.WaveFormat.BytesPerSecond / 2];
            soundInSource.DataAvailable += (s, _) =>
            {
                int read;
                while ((read = _finalSource.Read(inBuffer, 0, inBuffer.Length)) > 0)
                    _oggEncProcess.StandardInput.BaseStream.Write(inBuffer, 0, read);
            };

            _tcpListener = new AsyncTcpListener();
            _tcpListener.ClientConnected += (s, _) =>
            {
                _soundIn.Start();
            };
            _stdOut = new AsyncStreamChunker(_oggEncProcess.StandardOutput);
            _stdOut.DataReceived += (s, data) => _tcpListener.Write(data, 0, 512);
                _stdOut.Start();
        }
    }
    
    class AsyncStreamChunker
    {
        public delegate void EventHandler<args>(object sender, byte[] data);
        public event EventHandler<byte[]> DataReceived;

        protected byte[] _buffer;
        protected int _bufCount = 0;

        private StreamReader _reader;
        
        public bool Active { get; private set; }

        public void Start()
        {
            if (!Active)
            {
                Active = true;
                BeginReadAsync();
            }
        }

        public void Stop()
        {
            Active = false;
        }
        
        public AsyncStreamChunker(StreamReader reader, int chunkSize = 512)
        {
            _buffer = new byte[chunkSize];
            _reader = reader;
            Active = false;
        }
        
        protected void BeginReadAsync()
        {
            if (Active)
                _reader.BaseStream.BeginRead(_buffer, _bufCount, _buffer.Length - _bufCount, new AsyncCallback(ReadCallback), null);
        }

        private void ReadCallback(IAsyncResult asyncResult)
        {
            int bytesRead;

            if (!Active)
                return;

            bytesRead = _reader.BaseStream.EndRead(asyncResult);

            if (bytesRead <= 0)
            {
                Stop();
                return;
            }

            _bufCount += bytesRead;
            if (_bufCount == _buffer.Length)
            {
                if (DataReceived != null)
                    DataReceived.Invoke(this, _buffer);
                _bufCount = 0;
            }

            // wait for more data from stream
            BeginReadAsync();
        }
    }

    class AsyncTcpListener : Stream
    {
        private Socket _serverSocket;
        private Socket _clientSocket = null;
        public Socket ClientSocket { get => _clientSocket; }
        public event EventHandler ClientConnected;

        public override bool CanRead => throw new NotImplementedException();

        public override bool CanSeek => throw new NotImplementedException();

        public override bool CanWrite => true;

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public AsyncTcpListener(int port = 7777)
        {
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _serverSocket.Bind(localEndPoint);
            _serverSocket.Listen(1);
            _serverSocket.BeginAccept(new AsyncCallback(AcceptCallback), _serverSocket);
        }

        private void AcceptCallback(IAsyncResult result)
        {
            _clientSocket = _serverSocket.EndAccept(result);
            Console.WriteLine("client connected");
            ClientConnected.Invoke(this, null);
            _clientSocket.Receive(new byte[4096]);
        }

        private void SendCallback(IAsyncResult result)
        {
            Console.Write(".");
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_clientSocket == null)
                return;

            _clientSocket.BeginSend(buffer, offset, count, SocketFlags.None, new AsyncCallback(SendCallback), _clientSocket);
        }
    }
}