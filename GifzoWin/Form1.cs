using AForge.Video.FFMPEG;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Drawing.Imaging;

namespace GifzoWin
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            mode = Mode.Initializing;
        }

        private Point beginPoint;
        private Point endPoint;
        private int _width, _height;
        private int width
        { get { return mp4Mode ? _width / 2 * 2 : _width; } set { _width = value; } }
        private int height
        { get { return mp4Mode ? _height / 2 * 2 : _height; } set { _height = value; } }
        private System.Threading.Thread captureThread, getAsyncKeyStateThread, uploadThread;
        private bool mp4Mode = false;
        private bool movieMode
        { get { return (imageFormat != null && imageFormat == ImageFormat.Gif) || mp4Mode; } }
        public Setting setting;
        private string settingPath = "GifzoWin.config.xml";
        private string url = "http://gifzo.net/";
        public ImageFormat imageFormat;
        private string filename, filepath;
        private VideoFileWriter videoWriter;
        private List<MemoryStream> imageMemoryStreams;
        private int frameCount;
        private enum Mode { Null, Initializing, Loaded, StartDrag, Dragging, Ready, StartCapture, Capturing, EndCapture, Uploading, Uploaded, Exit }
        private string readyMessage
        {
            get
            {
                string result = "";
                if (setting.doUseModifierKey)
                { result = "ModifierKey + ".Replace("ModifierKey", Enum.GetName(typeof(Keys), setting.modifierKey)); }
                return result += "MainKey で収録開始".Replace("MainKey", Enum.GetName(typeof(Keys), setting.mainKey));
            }
        }
        private string capturingMessage
        {
            get
            {
                string result = "録画中...\n";
                if (setting.doUseModifierKey)
                { result += "ModifierKey + ".Replace("ModifierKey", Enum.GetName(typeof(Keys), setting.modifierKey)); }
                return result += "MainKey で収録終了".Replace("MainKey", Enum.GetName(typeof(Keys), setting.mainKey));
            }
        }
        private string uploadingMessage = "アップロード中...";
        private string cancelMessage = "キャンセル中...";
        private Mode _mode;
        private Mode mode
        {
            get { return _mode; }
            set
            {
                if (value != _mode || value == Mode.Dragging)
                {
                    _mode = value;
                    switch (value)
                    {
                        case Mode.Initializing:
                            Initializing();
                            break;
                        case Mode.Loaded:
                            Loaded();
                            break;
                        case Mode.StartDrag:
                            StartDrag();
                            break;
                        case Mode.Dragging:
                            Dragging();
                            break;
                        case Mode.Ready:
                            Ready();
                            break;
                        case Mode.StartCapture:
                            StartCapture();
                            break;
                        case Mode.Capturing:
                            Capturing();
                            break;
                        case Mode.EndCapture:
                            EndCapture();
                            break;
                        case Mode.Uploading:
                            Uploading();
                            break;
                        case Mode.Exit:
                            Exit();
                            break;
                        default:
                            Exit();
                            break;
                    }
                }
            }
        }

        #region モードが切り替わった
        private void Initializing()
        {
            //設定読み込み
            try
            {
                if (File.Exists(settingPath))
                {
                    System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(typeof(Setting));
                    using (FileStream fs = new FileStream(settingPath, FileMode.Open))
                    { setting = (Setting)serializer.Deserialize(fs); }
                }
                else
                { throw new Exception(); }
            }
            catch (Exception)
            {
                setting = new Setting();
                setting.milliseconds = 40;
                setting.toStringFormat = "yyyyMMddHHmmss";
                setting.mainKey = Keys.R;
                setting.doDeleteMp4File = true;
                setting.doUploadMp4FileToConvertToGif = true;
                setting.doUseModifierKey = true;
                setting.imageFormat = "MP4";
                setting.modifierKey = Keys.Control;
                setting.exitKey = Keys.Escape;
                setting.doShowContextMenu = false;
                setting.saveLocalLocation = System.Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                SaveSetting();
            }
            LoadedSettingCheck();
        }
        private void Loaded()
        {
            if (this.WindowState == FormWindowState.Maximized)
            {
                this.WindowState = FormWindowState.Normal;
                this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            }
            System.Diagnostics.Debug.WriteLine(Screen.PrimaryScreen.Bounds.Bottom);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Left = Screen.AllScreens.Select(screen => screen.Bounds.X).Min();
            this.Top = Screen.AllScreens.Select(screen => screen.Bounds.Y).Min();
            this.Width = Screen.AllScreens.Select(screen => screen.Bounds.Right).Max() - this.Left;
            this.Height = Screen.AllScreens.Select(screen => screen.Bounds.Bottom).Max() - this.Top;
            message.BackColor = captureBox.BackColor = Color.Black;
            message.Text = "";
            this.Opacity = 0.25;
            captureBox.Cursor = System.Windows.Forms.Cursors.Cross;
            getAsyncKeyStateThread = new System.Threading.Thread(new System.Threading.ThreadStart(GetAsyncKeyStateThread));
            getAsyncKeyStateThread.IsBackground = true;
            getAsyncKeyStateThread.Start();
        }
        private void StartDrag()
        {
            this.Location = beginPoint = Cursor.Position;
            this.WindowState = FormWindowState.Normal;
            this.Width = 1;
            this.Height = 1;
        }
        private void Dragging()
        {
            Point nowPoint = Cursor.Position;
            this.Width = Math.Abs(beginPoint.X - nowPoint.X);
            this.Height = Math.Abs(beginPoint.Y - nowPoint.Y);
            this.Location = new Point(Math.Min(beginPoint.X, nowPoint.X), Math.Min(beginPoint.Y, nowPoint.Y));
        }
        private void Ready()
        {
            captureBox.BorderStyle = BorderStyle.Fixed3D;
            message.Dock = DockStyle.None;
            message.Text = readyMessage;
            message.Top = (height - message.Height) / 2;
            message.Left = (width - message.Width) / 2;
            message.ForeColor = Color.White;
            captureBox.Cursor = System.Windows.Forms.Cursors.Default;
            this.TopMost = true;
        }
        private void StartCapture()
        {
            if (movieMode)
            {
                if (this.InvokeRequired) { Invoke(new FormCallback(FormStateCapturing)); }
                else { FormStateCapturing(); }
            }
            filename = DateTime.Now.ToString(setting.toStringFormat) + "." + (mp4Mode ? ".Mp4" : imageFormat.ToString());
            filepath = (mp4Mode && setting.doDeleteMp4File && setting.doUploadMp4FileToConvertToGif) ? Path.Combine(Path.GetTempPath(), filename) : Path.Combine(setting.saveLocalLocation, filename);
            if (mp4Mode)
            {
                videoWriter = new VideoFileWriter();
                videoWriter.Open(filepath, width, height, 1000 / setting.milliseconds, VideoCodec.MPEG4);
            }
            else
            { imageMemoryStreams = new List<MemoryStream>(); }
            mode = Mode.Capturing;
        }
        private void Capturing()
        {
            captureThread = new System.Threading.Thread(new System.Threading.ThreadStart(CaptureThread));
            captureThread.IsBackground = true;
            captureThread.Start();
        }
        private void EndCapture()
        {
            if (setting.doUploadMp4FileToConvertToGif)
            {
                if (this.InvokeRequired) { Invoke(new FormCallback(FormStateUploading)); }
                else { FormStateUploading(); }
            }
            while (movieMode && captureThread.IsAlive)
            { System.Threading.Thread.Sleep(setting.milliseconds); }
            if (mp4Mode)
            { videoWriter.Close(); }
            else if (imageFormat == ImageFormat.Gif)
            { AnimationGif.SaveAnimatedGif(filepath, imageMemoryStreams, (ushort)(setting.milliseconds / 10), 0); }
            else
            {
                using (FileStream fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
                { fs.Write(imageMemoryStreams[0].ToArray(), 0, (int)imageMemoryStreams[0].Length); }
            }
            if (mp4Mode && setting.doUploadMp4FileToConvertToGif)
            { mode = Mode.Uploading; }
            else
            { mode = Mode.Exit; }
        }
        private void Uploading()
        {
            uploadThread = new System.Threading.Thread(new System.Threading.ThreadStart(UploadThread));
            uploadThread.IsBackground = false;
            uploadThread.Start();
        }
        private void Exit()
        {
            if (mp4Mode)
            {
                if (mode != Mode.Uploaded)
                {
                    if (this.InvokeRequired) { Invoke(new FormCallback(FormStateCancel)); }
                    else { FormStateCancel(); }
                }
                //ファイルの削除
                if (mp4Mode && setting.doDeleteMp4File && setting.doUploadMp4FileToConvertToGif && filepath != null && System.IO.File.Exists(filepath))
                {
                    if (captureThread.IsAlive)
                    { System.Threading.Thread.Sleep(setting.milliseconds); }
                    if (videoWriter != null)
                    { videoWriter.Dispose(); }
                    if (uploadThread != null && uploadThread.IsAlive && mode != Mode.Uploaded)
                    {
                        uploadThread.Abort();
                        while (uploadThread.IsAlive)
                        { System.Threading.Thread.Sleep(setting.milliseconds); }
                    }
                    System.IO.File.Delete(filepath);
                }
                //スレッドの終了
                if (mode != Mode.Uploaded && uploadThread != null && uploadThread.IsAlive) { uploadThread.Abort(); }
            }
            //フォームを閉じる
            if (this.InvokeRequired) { Invoke(new FormCallback(FormClose)); }
            else { FormClose(); }
        }
        #endregion


        #region PrivateMethods
        private void SaveSetting()
        {
            System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(typeof(Setting));
            using (FileStream fs = new FileStream(settingPath, FileMode.Create))
            { serializer.Serialize(fs, setting); }
        }
        private void LoadedSettingCheck()
        {
            if (setting.mainKey == Keys.None) { setting.mainKey = Keys.R; }
            if (setting.modifierKey == Keys.None) { setting.modifierKey = Keys.Control; }
            if (setting.exitKey == Keys.None) { setting.exitKey = Keys.Escape; }
            switch (setting.imageFormat.ToUpper())
            {
                case "PNG":
                    mp4Mode = false;
                    imageFormat = ImageFormat.Png;
                    break;
                case "BMP":
                    mp4Mode = false;
                    imageFormat = ImageFormat.Bmp;
                    break;
                case "JPEG":
                    mp4Mode = false;
                    imageFormat = ImageFormat.Jpeg;
                    break;
                case "GIF":
                    mp4Mode = false;
                    imageFormat = ImageFormat.Gif;
                    break;
                default:
                    mp4Mode = true;
                    break;
            }
        }
        #endregion
        #region イベント
        private void Form1_Load(object sender, EventArgs e)
        { mode = Mode.Loaded; }
        private void captureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (mode == Mode.Loaded)
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                { mode = Mode.StartDrag; }
                else if (e.Button == System.Windows.Forms.MouseButtons.Right && setting.doShowContextMenu)
                { showContextMenu(); }
            }
        }
        private void captureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if ((mode == Mode.StartDrag || mode == Mode.Dragging) && e.Button == System.Windows.Forms.MouseButtons.Left)
            { mode = Mode.Dragging; }
        }
        private void captureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (mode == Mode.Dragging)
            {
                endPoint = Cursor.Position;
                width = Math.Abs(beginPoint.X - endPoint.X);
                height = Math.Abs(beginPoint.Y - endPoint.Y);
                if (width > 1 && height > 1)
                {
                    if (movieMode)
                    { mode = Mode.Ready; }
                    else
                    { mode = Mode.StartCapture; }
                }
                else
                { mode = Mode.Loaded; }
            }
        }
        #region ContextMenus
        private void showContextMenu()
        {
            foreach (ToolStripItem item in contextMenuStrip.Items)
            { if (item is ToolStripMenuItem) { (item as ToolStripMenuItem).Checked = false; } }
            if (mp4Mode && setting.doUploadMp4FileToConvertToGif)
            { uploadToGifzonetToolStripMenuItem.Checked = true; }
            else if (mp4Mode)
            { mpeg425fpsToolStripMenuItem.Checked = true; }
            else if (imageFormat == ImageFormat.Gif)
            { animationGif5fps.Checked = true; }
            else if (imageFormat == ImageFormat.Png)
            { pNGToolStripMenuItem.Checked = true; }
            else if (imageFormat == ImageFormat.Jpeg)
            { jPEGToolStripMenuItem.Checked = true; }
            else if (imageFormat == ImageFormat.Bmp)
            { bMPToolStripMenuItem.Checked = true; }
            contextMenuStrip.Show(Cursor.Position);
        }
        private void uploadToGifzonetToolStripMenuItem_Click(object sender, EventArgs e)
        { setting.imageFormat = "MP4"; setting.doUploadMp4FileToConvertToGif = true; setting.milliseconds = 40; ContextMenuClicked(); }
        private void animationGif5fps_Click(object sender, EventArgs e)
        { setting.imageFormat = "GIF"; setting.milliseconds = 200; ContextMenuClicked(); }
        private void mpeg425fpsToolStripMenuItem_Click(object sender, EventArgs e)
        { setting.imageFormat = "MP4"; setting.doUploadMp4FileToConvertToGif = false; setting.milliseconds = 40; ContextMenuClicked(); }
        private void pNGToolStripMenuItem_Click(object sender, EventArgs e)
        { setting.imageFormat = "PNG"; ContextMenuClicked(); }
        private void jPEGToolStripMenuItem_Click(object sender, EventArgs e)
        { setting.imageFormat = "JPEG"; ContextMenuClicked(); }
        private void bMPToolStripMenuItem_Click(object sender, EventArgs e)
        { setting.imageFormat = "BMP"; ContextMenuClicked(); }
        private void ContextMenuClicked()
        { SaveSetting(); LoadedSettingCheck(); Form1_Load(null, null); }
        #endregion
        #endregion

        #region スレッド
        private void CaptureThread()
        {
            frameCount = 0;
            while (mode == Mode.Capturing)
            {
                using (Bitmap bmp = new Bitmap(width, height))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(new Point(Math.Min(beginPoint.X, endPoint.X), Math.Min(beginPoint.Y, endPoint.Y)),
                        new Point(0, 0),
                        bmp.Size);
                    if (mp4Mode)
                    { videoWriter.WriteVideoFrame(bmp); }
                    else
                    {
                        MemoryStream ms = new MemoryStream();
                        bmp.Save(ms, imageFormat);
                        imageMemoryStreams.Add(ms);
                        if (imageFormat != ImageFormat.Gif)
                        { mode = Mode.EndCapture; }
                    }
                    frameCount += 1;
                }
                System.Threading.Thread.Sleep(setting.milliseconds);
            }
        }

        #region キーロガー
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetAsyncKeyState(int vKey);
        private void GetAsyncKeyStateThread()
        {
            while (mode == Mode.Loaded || mode == Mode.StartDrag ||
                mode == Mode.Dragging || mode == Mode.Ready ||
                mode == Mode.StartCapture || mode == Mode.Capturing ||
                mode == Mode.EndCapture || mode == Mode.Uploading)
            {
                if ((!setting.doUseModifierKey || Control.ModifierKeys == setting.modifierKey) && (GetAsyncKeyState((int)setting.mainKey) & 1) != 0)
                {
                    switch (mode)
                    {
                        case Mode.Ready:
                            mode = Mode.StartCapture;
                            break;
                        case Mode.Capturing:
                            mode = Mode.EndCapture;
                            break;
                        default:
                            break;
                    }
                }
                if ((GetAsyncKeyState((int)setting.exitKey) & 1) != 0)
                { mode = Mode.Exit; }
                System.Threading.Thread.Sleep(100);
            }
        }
        #endregion

        #region 別スレッドからのフォーム操作
        private delegate void FormCallback();
        private void FormClose()
        { this.Close(); }
        private void FormStateCapturing()
        {
            this.TransparencyKey = captureBox.BackColor;
            this.Opacity = 0.5;
            message.Text = "";
            notifyIcon.BalloonTipText = capturingMessage;
            notifyIcon.ShowBalloonTip(10, this.Text, notifyIcon.BalloonTipText, ToolTipIcon.Info);
        }
        private void FormStateUploading()
        {
            this.TransparencyKey = captureBox.BackColor = message.BackColor = Color.White;
            notifyIcon.BalloonTipText = message.Text = uploadingMessage;
            message.ForeColor = Color.Black;
            notifyIcon.ShowBalloonTip(10, this.Text, notifyIcon.BalloonTipText, ToolTipIcon.Info);
        }
        private void FormStateCancel()
        {
            this.TransparencyKey = captureBox.BackColor = message.BackColor = Color.White;
            notifyIcon.BalloonTipText = message.Text = cancelMessage;
            message.ForeColor = Color.Black;
            notifyIcon.ShowBalloonTip(10, this.Text, notifyIcon.BalloonTipText, ToolTipIcon.Info);
        }
        #endregion

        private void UploadThread()
        {
            try
            {
                System.Text.Encoding enc = System.Text.Encoding.GetEncoding("UTF-8");
                System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(url);
                req.AutomaticDecompression = System.Net.DecompressionMethods.GZip;
                req.Method = "POST";
                string boundary = "OchinchinNametaiyo-u" + System.Environment.TickCount.ToString();
                req.ContentType = "multipart/form-data; boundary=" + boundary;
                byte[] startData = enc.GetBytes(
                    "--" + boundary + "\r\n" +
                    "Content-Disposition: form-data; name=\"data\"; filename=\"" +
                        filename + "\"\r\n" +
                    "Content-Type: application/octet-stream\r\n" +
                    "Content-Transfer-Encoding: binary\r\n\r\n"
                    );
                byte[] endData = enc.GetBytes("\r\n--" + boundary + "--\r\n");
                using (FileStream fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
                {
                    req.ContentLength = startData.Length + fs.Length + endData.Length;
                    using (Stream reqStream = req.GetRequestStream())
                    {
                        reqStream.Write(startData, 0, startData.Length);
                        byte[] readData = new byte[0x1000];
                        int readSize = 0;
                        while (true)
                        {
                            readSize = fs.Read(readData, 0, readData.Length);
                            if (readSize == 0) { break; }
                            reqStream.Write(readData, 0, readSize);
                        }
                        reqStream.Write(endData, 0, endData.Length);
                        System.Net.HttpWebResponse res = (System.Net.HttpWebResponse)req.GetResponse();
                        using (Stream resStream = res.GetResponseStream())
                        using (StreamReader sr = new StreamReader(resStream, enc))
                        {
                            System.Diagnostics.Process.Start(sr.ReadToEnd());
                        }
                    }
                }
            }
            catch (System.Threading.ThreadAbortException)
            { return; }
            catch (Exception ex)
            { MessageBox.Show(ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally
            {
                mode = Mode.Uploaded;
                Exit();
            }
        }
        #endregion

    }

    public class Setting
    {
        public int milliseconds { get; set; }
        public Keys mainKey { get; set; }
        public bool doUseModifierKey { get; set; }
        public Keys modifierKey { get; set; }
        public Keys exitKey { get; set; }
        public string imageFormat { get; set; }
        public string toStringFormat { get; set; }
        public bool doUploadMp4FileToConvertToGif { get; set; }
        public bool doDeleteMp4File { get; set; }
        public bool doShowContextMenu { get; set; }
        public string saveLocalLocation { get; set; }
        public string note { get { return _note; } set { } }
        private string _note =
            "GifzoWin and Giflo Setting Note  \n\n" +
            "imageFormat accepts PNG, GIF, BMP, JPEG, and MP4. \n" +
            "When imageFormat is GIF or MP4, the program works as Movie-Mode. \n" +
            "On Movie-Mode, need to set mainKey and modifierKey(if doUseModifierKey) to start and stop capture. \n" +
            "When imageFormat is MP4 and doUploadMp4FileToConvertToGif is true, the program works as Gifzo-Mode. \n" +
            "The program automatically upload mp4 file to Gifzo.net, ant convert to Animation-Gif. \n" +
            "When Gifzo-Mode, if doDeleteMp4File, the mp4 file will be deleted. \n" +
            "On other modes, the captured file will save to current directory, filename is formated as toStringFormat.";
    }

    class AnimationGif
    {
        public static void SaveAnimatedGif(string fileName, List<MemoryStream> gmss, UInt16 delayTime, UInt16 loopCount)
        {
            using (FileStream writerFs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(writerFs))
            {
                bool hasGlobalColorTable = false;
                int colorTableSize = 0;
                int imagesCount = gmss.Count;
                for (int i = 0; i < imagesCount; i++)
                {
                    using (MemoryStream ms = gmss[i])
                    {
                        ms.Position = 0;

                        if (i == 0)
                        {
                            writer.Write(ReadBytes(ms, 6));
                            byte[] screenDescriptor = ReadBytes(ms, 7);
                            if ((screenDescriptor[4] & 0x80) != 0)
                            {
                                colorTableSize = screenDescriptor[4] & 0x07;
                                hasGlobalColorTable = true;
                            }
                            else
                            { hasGlobalColorTable = false; }
                            screenDescriptor[4] = (byte)(screenDescriptor[4] & 0x78);
                            writer.Write(screenDescriptor);
                            writer.Write(GetAppricationExtension(loopCount));
                        }
                        else
                        { ms.Position += 6 + 7; }

                        byte[] colorTable = null;
                        if (hasGlobalColorTable)
                        { colorTable = ReadBytes(ms, (int)Math.Pow(2, colorTableSize + 1) * 3); }
                        writer.Write(GetGraphicControlExtension(delayTime));
                        if (ms.GetBuffer()[ms.Position] == 0x21)
                        { ms.Position += 8; }
                        byte[] imageDescriptor = ReadBytes(ms, 10);
                        if (!hasGlobalColorTable)
                        {
                            if ((imageDescriptor[9] & 0x80) == 0)
                            { throw new Exception("Color table not found."); }
                            colorTableSize = imageDescriptor[9] & 7;
                            colorTable = ReadBytes(ms, (int)Math.Pow(2, colorTableSize + 1) * 3);
                        }
                        imageDescriptor[9] = (byte)(imageDescriptor[9] | 0x80 | colorTableSize);
                        writer.Write(imageDescriptor);
                        writer.Write(colorTable);
                        writer.Write(ReadBytes(ms, (int)(ms.Length - ms.Position - 1)));
                        if (i == (imagesCount - 1))
                        { writer.Write((byte)0x3B); }
                        ms.SetLength(0);
                    }
                }
            }
        }
        private static byte[] ReadBytes(MemoryStream ms, int count)
        {
            byte[] bs = new byte[count];
            ms.Read(bs, 0, count);
            return bs;
        }
        private static byte[] GetAppricationExtension(UInt16 loopCount)
        {
            byte[] loopCountBytes = BitConverter.GetBytes(loopCount);
            List<char> chars = new List<char> { 'N', 'E', 'T', 'S', 'C', 'A', 'P', 'E', '2', '.', '0' };
            List<byte> p = new List<byte> { 0x21, 0xFF, 0x0B };
            p.AddRange(chars.ConvertAll<byte>(i => (byte)i));
            p.AddRange(new List<byte> { 0x03, 0x01, loopCountBytes[0], loopCountBytes[1], 0x00 });
            return p.ToArray();

        }
        private static byte[] GetGraphicControlExtension(UInt16 delayTime)
        {
            byte[] delayBytes = BitConverter.GetBytes(delayTime);
            return new byte[] { 0x21, 0xF9, 0x04, 0x00, delayBytes[0], delayBytes[1], 0x00, 0x00 };
        }
    }
}
