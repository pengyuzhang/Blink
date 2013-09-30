using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Net;
using System.Diagnostics;
using System.Xml;
using System.Timers;

using LLRP;
using ReaderLibrary;
using LLRP.DataType;

namespace ReaderLibrary
{
    public class ReaderManager
    {
        private RFIDReader reader;
        private ITagHandler handleTags;
       
        //Create an instance of LLRP client client.
        //public LLRPClient client = new LLRPClient();

        // Store reader-capable modulation modes
        public ReaderMode[] readerModes;

        //Configuration
        public ReaderConfig readerconfig = new ReaderConfig();
        protected InventoryConfig inventoryconfig = new InventoryConfig();
        protected ReadCmdSettings readcmdsettings = new ReadCmdSettings();

        private GuiModes currentMode;
        public enum GuiModes
        {
            Idle,  // reader disconnected.
            Ready, // reader is connected.
            UserInventory,
            AttenuatorTest
        }

        // Parameters for mobility detection
        System.Timers.Timer probe_channel_timer;
        public Boolean stopInventory = false;
        public struct TagInfo
        {
            public int[] rssi;
        }
        public TagInfo tagInfo = new TagInfo();
        public int mobility_probe_round;
        public int[,] mobility_pattern;
        public double mobility_rssi_diff;
        public double mobility_fading_diff;
        public int channel_index = 1;
        public int last_channel_index = 1;
        public int channel_counter = 0;

        // Parameters for rate adaptation
        public double[,] channelTemplate;
        public short[,] rssiTemplate;

        // Read/Write access settings can be added from ready or userinventory states.
        // So just keep as a separate variable.
        private bool readConfigured = false;

        private string reader_address;
        
        private IRFIDGUI gui;

        public ReaderManager(IRFIDGUI newgui, ITagHandler handleTagNew)
        {
            gui = newgui;
            SetDefaultReaderConfig();
            SetDefaultInventoryConfig();
            reader = new RFIDReader(this);
            handleTags = handleTagNew;

            // Initialization
            mobility_probe_round = 0;
            mobility_rssi_diff = 0;
            mobility_fading_diff = 0;

            mobility_pattern = new int[2, 50];
            tagInfo.rssi = new int[50];
            for (int ii = 0; ii < 50; ii++)
            {
                tagInfo.rssi[ii] = 0;
            }
            channel_counter = 0;
            // Load channel/rssi template information
            channelTemplate = new double[7, 50];
            rssiTemplate = new short[7, 50];
            using (StreamReader sr = new StreamReader("loss_fast_probe.dat"))
            {
                String line;
                int lineNum = 0;
                line = sr.ReadLine();
                while (line != null)
                {
                    String[] lineSplit = line.Split();
                    for (int ii = 0; ii < 50; ii++)
                    {
                        channelTemplate[lineNum, ii] = Convert.ToDouble(lineSplit[ii]);
                    }
                    lineNum += 1;
                    line = sr.ReadLine();
                }
            }
            using (StreamReader sr = new StreamReader("rssi_fast_probe.dat"))
            {
                String line;
                int lineNum = 0;
                line = sr.ReadLine();
                while (line != null)
                {
                    String[] lineSplit = line.Split();
                    for (int ii = 0; ii < 50; ii++)
                    {
                        rssiTemplate[lineNum, ii] = Convert.ToInt16(lineSplit[ii]);
                    }
                    lineNum += 1;
                    line = sr.ReadLine();
                }
            }
        }

        // Helper method to connect Reader to MainForm
        public void HandleTagReceived(MyTag tag) 
        {
            if (tag == null)
            {
                // todo - some gui display thing
                //AppendToMainTextBox("No Tags Seen");
            }
            else if (IsInventoryRunning())  // if reader is running
            {
                handleTags.HandleTagReceived(tag);
                channel_index = Convert.ToInt32(tag.GetFrequency());
                if (channel_index == last_channel_index)
                {
                    tagInfo.rssi[channel_index - 1] = tagInfo.rssi[channel_index - 1] + Convert.ToInt32(tag.GetRSSI());
                    channel_counter = channel_counter + 1;
                }
                else {
                    if (channel_counter != 0)
                    {
                        tagInfo.rssi[last_channel_index - 1] = tagInfo.rssi[last_channel_index - 1] / channel_counter;
                        tagInfo.rssi[channel_index - 1] = Convert.ToInt32(tag.GetRSSI());
                        channel_counter = 1;
                        last_channel_index = channel_index;
                    }
                    else {
                        tagInfo.rssi[channel_index - 1] = Convert.ToInt32(tag.GetRSSI());
                        channel_counter = 1;
                        last_channel_index = channel_index;
                    }
                }
            }
        }

        public void AppendToDebugTextBox(string message)
        {
            gui.AppendToDebugTextBox(message);
        }

        #region Public Reader Interfaces


        public GuiModes getCurrentMode()
        {
            return currentMode;
        }

        public void SetMode(GuiModes newMode, string txtIPAddress)
        {
            // Set current mode = new Mode,
            // this will be changed if there is an error.
            GuiModes oldMode = currentMode;

            if(newMode == GuiModes.AttenuatorTest)
                throw new Exception("Can't set mode to AttenuatorTest!");

            // Switch performs the action
            switch (newMode)
            {
                case GuiModes.Idle:
                    if (IsConnected())
                    {
                        Disconnect();
                    }
                    break;

                case GuiModes.Ready:
                    // If we were disconnected, connect.
                    if (oldMode == GuiModes.Idle)
                    {
                        if (!IsConnected())
                        {
                            // CONNECT!
                            Connect(txtIPAddress);
                            // if connect fails, an exception will be thrown
                            // if success, currentMode will be set after this switch statement
                        }
                    }
                    else if (oldMode == GuiModes.Ready)
                    {
                        stopInventory = true;
                    }
                    // If we were running, stop various modes:
                    else if (oldMode == GuiModes.UserInventory)
                    {
                        if (IsConnected())
                        {
                            StopInventory();
                        }
                    }
                    break;


                case GuiModes.UserInventory:
                    if (!IsConnected())
                    {
                        currentMode = GuiModes.Idle;
                    }
                    else if (oldMode == GuiModes.Ready)
                    {
                        StartInventory();
                    }
                    break;

                default: 
                    throw new Exception("Can't set mode to unknown state!");
                    //break;
            }

            // if no exceptions have interrupted us, we successfully got to the new mode:
            currentMode = newMode;

        }



        /// <summary>
        /// Connect to Reader
        /// </summary>
        public void Connect(string ipAddress)
        {
            reader_address = ipAddress;
            // check for dumb errors.
            if (IsConnected())
                throw new Exception("Already connected");

            if (ipAddress.Length == 0)
                throw new Exception("Bad IP");

            IPAddress address = null;

            // look for url-based addresses
            if (ipAddress.ToLower().Contains("speedway"))
            {
                IPAddress[] addresses;
                addresses = Dns.GetHostEntry(ipAddress).AddressList;

                if (addresses.Length >= 1)
                    address = addresses[0];
                else
                    throw new Exception("Hostname not found.");
            }
            else
            {
                if (!System.Net.IPAddress.TryParse(ipAddress, out address))
                    throw new Exception("Bad IP Address");
                address = System.Net.IPAddress.Parse(ipAddress);
            }

            if (address != null)
            {
                bool connect =  reader.connectTo(ipAddress);
                if (connect)
                {
                    currentMode = GuiModes.Ready;

                    readerModes = reader.Get_Reader_Capability();
                    SetDefaultReaderConfig();
                    SetDefaultInventoryConfig();

                    //WriteMessage(status.ToString());

                    reader.Initialize();
                }
                else
                {
                    throw new Exception("Bad IP or Reader in use.");
                }
            }

            else
            {
                // MessageBox.Show("Need IP address to connect to client", "LLRP Test", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // txtIPAddress.Focus();
                throw new Exception("Need IP address to connect to client");
            }

        }

        
        /// <summary>
        /// disconnect to Reader
        /// </summary>
        public void Disconnect()
        {
            // check if doing inventory
            if (IsInventoryRunning())
            {
                StopInventory();
            }

            // check if doing read
            if (IsReadRunning())
            {
                StopRead();
            }

            if(IsConnected())
            {
                reader.CleanSubscriptionClient();
            }

            currentMode = GuiModes.Idle;

        }

        public void StartInventory()
        {
            if (!IsConnected())
            {
                gui.AppendToDebugTextBox("Can't start inventory: Reader not connected.");
                return;
            }

            // Initialize the reader configuration to obtain another mobility pattern vector
            // Clean current reader configuration
            reader.CleanSubscriptionClient();
            currentMode = GuiModes.Idle;

            // Initialize the reader
            bool connect = reader.connectTo(reader_address);
            currentMode = GuiModes.Ready;
            readerModes = reader.Get_Reader_Capability();
            SetDefaultReaderConfig();
            SetDefaultInventoryConfig();
            reader.Initialize();

            stopInventory = false;

            ProbeMobilityPatterns();
        }

        public void ProbeMobilityPatterns()
        {
            // Stop reader inventory
            if (IsInventoryRunning())
            {
                reader.Stop_RoSpec();
                reader.Delete_RoSpec();
                currentMode = GuiModes.Ready;
            }

            if (probe_channel_timer != null)
            {
                probe_channel_timer.Close();
            }

            // Mobility probe mode
            for (int ii = 0; ii < 50; ii++)
            {
                tagInfo.rssi[ii] = 0;
            }
            channel_counter = 0;

            /*
            // Initialize the reader configuration to obtain another mobility pattern vector
            // Clean current reader configuration
            reader.CleanSubscriptionClient();
            currentMode = GuiModes.Idle;

            // Initialize the reader
            bool connect = reader.connectTo("10.0.0.200");
            currentMode = GuiModes.Ready;
            readerModes = reader.Get_Reader_Capability();
            SetDefaultReaderConfig();
            SetDefaultInventoryConfig();
            reader.Initialize();*/

            inventoryconfig.duration = 30;
            readerconfig.modeIndex = 1000;
            reader.Set_Reader_Config(readerconfig);   // Sets the client configuration

            currentMode = GuiModes.UserInventory;

            // Add a ROSpec
            reader.Add_RoSpec(inventoryconfig, readerconfig);
            reader.Enable_RoSpec();

            // Start the first round of mobility detection
            if (stopInventory == false)
            {
                probe_channel_timer = new System.Timers.Timer();
                probe_channel_timer.Elapsed += new System.Timers.ElapsedEventHandler(RecordMobilityPatterns);
                probe_channel_timer.Interval = 1500;
                probe_channel_timer.Enabled = true;
            }
        }

        public void RecordMobilityPatterns(Object sender, ElapsedEventArgs e)
        {
            // Stop reader inventory
            if (IsInventoryRunning())
            {
                reader.Stop_RoSpec();
                reader.Delete_RoSpec();
                currentMode = GuiModes.Ready;
            }
            if (probe_channel_timer != null)
            {
                probe_channel_timer.Close();
            }

            if (mobility_probe_round == 0)
            {
                if (channel_index == last_channel_index)
                {
                    if (channel_counter != 0)
                    {
                        tagInfo.rssi[channel_index - 1] = tagInfo.rssi[channel_index - 1] / channel_counter;
                    }
                }
                // Record the first round of tag mobility detection
                for (int ii = 0; ii < 50; ii++) {
                    mobility_pattern[0, ii] = tagInfo.rssi[ii];
                }

                mobility_probe_round = mobility_probe_round + 1;

                ProbeMobilityPatterns();
            }
            else {
                if (channel_index == last_channel_index)
                {
                    if (channel_counter!=0)
                    {
                        tagInfo.rssi[channel_index - 1] = tagInfo.rssi[channel_index - 1] / channel_counter;
                    }
                }
                // Record the second round of tag mobility detection
                for (int ii = 0; ii < 50; ii++)
                {
                    mobility_pattern[1, ii] = tagInfo.rssi[ii];
                }

                Thread.Sleep(10);
                // Check tag mobility pattern
                Boolean tag_mobility = CheckTagMobility();

                // Finish mobility detection and switch to rate adaptation
                // Probe channel first
                for (int ii = 0; ii < 50; ii++)
                {
                    tagInfo.rssi[ii] = 0;
                }
                channel_counter = 0;
                inventoryconfig.duration = 20;
                readerconfig.modeIndex = 1000;
                reader.Set_Reader_Config(readerconfig);   // Sets the client configuration

                currentMode = GuiModes.UserInventory;

                // Add a ROSpec
                reader.Add_RoSpec(inventoryconfig, readerconfig);
                reader.Enable_RoSpec();

                if (tag_mobility==false)
                {                                   
                    // For static tag
                    if (stopInventory == false)
                    {
                        probe_channel_timer = new System.Timers.Timer();
                        probe_channel_timer.Elapsed += new System.Timers.ElapsedEventHandler(RateAdaptation_SlowMovingTag);
                        probe_channel_timer.Interval = 1000;
                        probe_channel_timer.Enabled = true;
                    }
                }
                else {                              
                    // For mobile tag 
                    if (stopInventory == false)
                    {
                        probe_channel_timer = new System.Timers.Timer();
                        probe_channel_timer.Elapsed += new System.Timers.ElapsedEventHandler(RateAdaptation_FastMovingTag);
                        probe_channel_timer.Interval = 1000;
                        probe_channel_timer.Enabled = true;
                    }
                }
            }
            return;
        }

        // Output: false/static tag, true/mobile tag
        public Boolean CheckTagMobility()
        {
            double counter_rssi = 0;
            double counter_fading = 0;
            double diff = 0;

            for (int ii = 0; ii < 50; ii++)
            {
                if (mobility_pattern[0, ii] != 0 && mobility_pattern[1, ii] != 0)
                {
                    diff = diff + Math.Pow(mobility_pattern[0, ii] - mobility_pattern[1, ii], 2);
                    counter_rssi = counter_rssi + 1;
                }
                if ((mobility_pattern[0, ii] != 0 && mobility_pattern[1, ii] == 0) || (mobility_pattern[0, ii] == 0 && mobility_pattern[1, ii] != 0))
                {
                    counter_fading = counter_fading + 1;
                }
            }

            // Get the mobility detection result
            mobility_rssi_diff = 0;
            if (counter_rssi != 0)
            {
                mobility_rssi_diff = Math.Sqrt(diff) / counter_rssi;
            }
            if ((counter_rssi + counter_fading) != 0)
            {
                mobility_fading_diff = counter_fading / (counter_fading + counter_rssi);
            }

            if (mobility_fading_diff < 0.15 && mobility_rssi_diff < 0.3)
            {
                for (int ii = 0; ii < 50; ii++)
                {
                    mobility_pattern[0, ii] = mobility_pattern[1, ii];
                }
                return false;
            }
            else {
                for (int ii = 0; ii < 50; ii++)
                {
                    mobility_pattern[0, ii] = mobility_pattern[1, ii];
                }
                return true;
            }
        }

        public void RateAdaptation_SlowMovingTag(Object sender, ElapsedEventArgs e)
        {
            // Stop reader inventory
            if (IsInventoryRunning())
            {
                reader.Stop_RoSpec();
                reader.Delete_RoSpec();
                currentMode = GuiModes.Ready;
            }

            if (probe_channel_timer != null)
            {
                probe_channel_timer.Close();
            }

            readerconfig.modeIndex = RateSelection();
            for (int ii = 0; ii < 50; ii++)
            {
                tagInfo.rssi[ii] = 0;
            }
            channel_counter = 0;
            inventoryconfig.duration = 100;
            reader.Set_Reader_Config(readerconfig);   // Sets the client configuration

            currentMode = GuiModes.UserInventory;

            // Add a ROSpec
            reader.Add_RoSpec(inventoryconfig, readerconfig);
            reader.Enable_RoSpec();

            if (stopInventory == false)
            {
                probe_channel_timer = new System.Timers.Timer();
                probe_channel_timer.Elapsed += new System.Timers.ElapsedEventHandler(SlowMovingTag);
                probe_channel_timer.Interval = 5000;
                probe_channel_timer.Enabled = true;
            }

            return;
        }

        public void SlowMovingTag(Object sender, ElapsedEventArgs e)
        {
            // Stop reader inventory
            if (IsInventoryRunning())
            {
                reader.Stop_RoSpec();
                reader.Delete_RoSpec();
                currentMode = GuiModes.Ready;
            }

            if (probe_channel_timer != null)
            {
                probe_channel_timer.Close();
            }

            if (channel_index == last_channel_index)
            {
                if (channel_counter != 0)
                {
                    tagInfo.rssi[channel_index - 1] = tagInfo.rssi[channel_index - 1] / channel_counter;
                }
            }
            // Record the second round of tag mobility detection
            for (int ii = 0; ii < 50; ii++)
            {
                mobility_pattern[0, ii] = tagInfo.rssi[ii];
            }
            for (int ii = 0; ii < 50; ii++)
            {
                tagInfo.rssi[ii] = 0;
            }
            channel_counter = 0;

            inventoryconfig.duration = 100;
            reader.Set_Reader_Config(readerconfig);   // Sets the client configuration

            currentMode = GuiModes.UserInventory;

            // Add a ROSpec
            reader.Add_RoSpec(inventoryconfig, readerconfig);
            reader.Enable_RoSpec();

            if (stopInventory == false)
            {
                probe_channel_timer = new System.Timers.Timer();
                probe_channel_timer.Elapsed += new System.Timers.ElapsedEventHandler(SlowMovingTag_MobilityCheck);
                probe_channel_timer.Interval = 5000;
                probe_channel_timer.Enabled = true;
            }

            return;
        }

        // SlowMovingTag_MobilityCheck use history record to figure out the mobility patterns of slowing moving tag.
        public void SlowMovingTag_MobilityCheck(Object sender, ElapsedEventArgs e)
        {
            // Stop reader inventory
            if (IsInventoryRunning())
            {
                reader.Stop_RoSpec();
                reader.Delete_RoSpec();
                currentMode = GuiModes.Ready;
            }
            if (probe_channel_timer != null) {
                probe_channel_timer.Close();
            }

            if (channel_index == last_channel_index)
            {
                if (channel_counter != 0)
                {
                    tagInfo.rssi[channel_index - 1] = tagInfo.rssi[channel_index - 1] / channel_counter;
                }
            }
            // Record the second round of tag mobility detection
            for (int ii = 0; ii < 50; ii++)
            {
                mobility_pattern[1, ii] = tagInfo.rssi[ii];
            }

            Thread.Sleep(10);
            Boolean tag_mobility = CheckTagMobility();

            if (tag_mobility == false)
            {
                for (int ii = 0; ii < 50; ii++)
                {
                    tagInfo.rssi[ii] = 0;
                }
                channel_counter = 0;
                inventoryconfig.duration = 100;
                reader.Set_Reader_Config(readerconfig);   // Sets the client configuration

                currentMode = GuiModes.UserInventory;

                // Add a ROSpec
                reader.Add_RoSpec(inventoryconfig, readerconfig);
                reader.Enable_RoSpec();

                // For static tag
                if (stopInventory == false)
                {
                    probe_channel_timer = new System.Timers.Timer();
                    probe_channel_timer.Elapsed += new System.Timers.ElapsedEventHandler(SlowMovingTag_MobilityCheck);
                    probe_channel_timer.Interval = 5000;
                    probe_channel_timer.Enabled = true;
                }
            }
            else
            {
                // Rate adaptation and switch to mobile data transfer mode.
                for (int ii = 0; ii < 50; ii++)
                {
                    tagInfo.rssi[ii] = 0;
                }
                channel_counter = 0;
                inventoryconfig.duration = 20;
                readerconfig.modeIndex = 1000;
                reader.Set_Reader_Config(readerconfig);   // Sets the client configuration

                currentMode = GuiModes.UserInventory;

                // Add a ROSpec
                reader.Add_RoSpec(inventoryconfig, readerconfig);
                reader.Enable_RoSpec();

                // For mobile tag 
                if (stopInventory == false)
                {
                    probe_channel_timer = new System.Timers.Timer();
                    probe_channel_timer.Elapsed += new System.Timers.ElapsedEventHandler(RateAdaptation_FastMovingTag);
                    probe_channel_timer.Interval = 1000;
                    probe_channel_timer.Enabled = true;
                }
            }
            return;
        }

        public void RateAdaptation_FastMovingTag(Object sender, ElapsedEventArgs e)
        {
            // Stop reader inventory
            if (IsInventoryRunning())
            {
                reader.Stop_RoSpec();
                reader.Delete_RoSpec();
                currentMode = GuiModes.Ready;
            }

            if (probe_channel_timer != null)
            {
                probe_channel_timer.Close();
            }
            readerconfig.modeIndex = RateSelection();
            
            // Start transmission
            for (int ii = 0; ii < 50; ii++)
            {
                tagInfo.rssi[ii] = 0;
            }
            //channel_counter = 0;
            inventoryconfig.duration = 100;
            reader.Set_Reader_Config(readerconfig);   // Sets the client configuration

            currentMode = GuiModes.UserInventory;

            // Add a ROSpec
            reader.Add_RoSpec(inventoryconfig, readerconfig);
            reader.Enable_RoSpec();

            if (stopInventory == false)
            {
                probe_channel_timer = new System.Timers.Timer();
                probe_channel_timer.Elapsed += new System.Timers.ElapsedEventHandler(FastMovingTag_MobilityCheck);
                probe_channel_timer.Interval = 5000;
                probe_channel_timer.Enabled = true;
            }

            return;
        }

        // FastMovingTag_MobilityCheck use history record to figure out the mobility patterns of fast moving tag.
        public void FastMovingTag_MobilityCheck(Object sender, ElapsedEventArgs e)
        {
            // Stop reader inventory
            if (IsInventoryRunning())
            {
                reader.Stop_RoSpec();
                reader.Delete_RoSpec();
                currentMode = GuiModes.Ready;
            }
            if (probe_channel_timer != null)
            {
                probe_channel_timer.Close();
            }

            if (channel_index == last_channel_index)
            {
                if (channel_counter != 0)
                {
                    tagInfo.rssi[channel_index - 1] = tagInfo.rssi[channel_index - 1] / channel_counter;
                }
            }
            // Record the second round of tag mobility detection
            for (int ii = 0; ii < 50; ii++)
            {
                mobility_pattern[1, ii] = tagInfo.rssi[ii];
            }

            Thread.Sleep(10);
            Boolean tag_mobility = CheckTagMobility();

            if (tag_mobility == false)
            {
                // Rate adaptation and switch to mobile data transfer mode.
                for (int ii = 0; ii < 50; ii++)
                {
                    tagInfo.rssi[ii] = 0;
                }
                //channel_counter = 0;
                inventoryconfig.duration = 20;
                readerconfig.modeIndex = 1000;
                reader.Set_Reader_Config(readerconfig);   // Sets the client configuration

                currentMode = GuiModes.UserInventory;

                // Add a ROSpec
                reader.Add_RoSpec(inventoryconfig, readerconfig);
                reader.Enable_RoSpec();

                // For mobile tag 
                if (stopInventory == false)
                {
                    probe_channel_timer = new System.Timers.Timer();
                    probe_channel_timer.Elapsed += new System.Timers.ElapsedEventHandler(RateAdaptation_SlowMovingTag);
                    probe_channel_timer.Interval = 1000;
                    probe_channel_timer.Enabled = true;
                }
            }
            else
            {
                // Probe channel first
                for (int ii = 0; ii < 50; ii++)
                {
                    tagInfo.rssi[ii] = 0;
                }
                //channel_counter = 0;
                inventoryconfig.duration = 20;
                readerconfig.modeIndex = 1000;
                reader.Set_Reader_Config(readerconfig);   // Sets the client configuration

                currentMode = GuiModes.UserInventory;

                // Add a ROSpec
                reader.Add_RoSpec(inventoryconfig, readerconfig);
                reader.Enable_RoSpec();

                if (stopInventory == false)
                {
                    probe_channel_timer = new System.Timers.Timer();
                    probe_channel_timer.Elapsed += new System.Timers.ElapsedEventHandler(RateAdaptation_FastMovingTag);
                    probe_channel_timer.Interval = 1000;
                    probe_channel_timer.Enabled = true;
                }
            }
        }

        // Select modulation and baud rate based on current channel condition
        public ushort RateSelection() {
            int ii, jj, n;
            // Arrange current rssi and channel info into two arrays.
            int[] rssi = new int[50];
            double[] channel = new double[50];
            for (ii = 0; ii < 50; ii++)
            {
                rssi[ii] = tagInfo.rssi[ii];
                if (rssi[ii] != 0)
                {
                    channel[ii] = 1;
                }
                else {
                    channel[ii] = 0;
                }
            }

            // Sort rssi and channel info.
            // Move 0s to the end of rssi array
            
            ii=0;
            jj=49;
            while (ii < jj) {
                if (rssi[ii] != 0) {
                    ii = ii + 1;
                }
                else if (rssi[ii] == 0 && rssi[jj] != 0) {
                    int tmp = rssi[jj];
                    rssi[jj] = rssi[ii];
                    rssi[ii] = tmp;
                    ii = ii + 1;
                    jj = jj - 1;
                }
                else if (rssi[ii] == 0 && rssi[jj] == 0) {
                    jj = jj - 1;
                }
            }
            if (ii == jj) {
                n = ii - 1;
                n = n + 1;
            }
            else {
                n = jj;
                n = n + 1;
            }
            
            while (n > 1)
            {
                int mark = 0;
                for (ii = 0; ii < n - 1; ii++)
                {
                    if (rssi[ii] < rssi[ii + 1])
                    {
                        int tmp = rssi[ii];
                        rssi[ii] = rssi[ii + 1];
                        rssi[ii + 1] = tmp;
                    }
                    mark = ii + 1;
                }
                n = mark;
            }

            n = 50;
            while (n > 1)
            {
                int mark = 0;
                for (ii = 0; ii < n - 1; ii++)
                {
                    if (channel[ii] < channel[ii + 1])
                    {
                        double tmp = channel[ii];
                        channel[ii] = channel[ii + 1];
                        channel[ii + 1] = tmp;
                    }
                    mark = ii + 1;
                }
                n = mark;
            }

            // Find the first position where either rssi or rssiTemplate reaches a zero

            double[] score = new double[7] { 0, 0, 0, 0, 0, 0, 0 };
            for (ii = 0; ii < 7; ii++)
            {
                for (jj = 0; jj < 50; jj++)
                {
                    if (rssi[jj] != 0 && rssiTemplate[ii, jj] != 0)
                    {
                        score[ii] = score[ii] + Math.Abs(rssi[jj] - rssiTemplate[ii, jj]) / 80;
                    }
                }
                for (jj = 0; jj < 50; jj++)
                {
                    score[ii] = score[ii] + Math.Abs(channel[jj] - channelTemplate[ii, jj]);
                }
            }

            double minScore = score[0];
            int minScoreP = 0;
            for (ii = 0; ii < 7; ii++)
            {
                if (score[ii] <= minScore)
                {
                    minScore = score[ii];
                    minScoreP = ii;
                }
            }

            if (minScoreP == 0)
            {
                //readerconfig.modeIndex = 0;
                return 0;
            }
            else if (minScoreP == 1)
            {
                //readerconfig.modeIndex = 4;
                return 4;
            }
            else if (minScoreP == 2)
            {
                //readerconfig.modeIndex = 1000;
                return 1000;
            }
            else if (minScoreP == 3)
            {
                //readerconfig.modeIndex = 4;
                return 4;
            }
            else if (minScoreP == 4)
            {
                //readerconfig.modeIndex = 4;
                return 4;
            }
            else if (minScoreP == 5)
            {
                //readerconfig.modeIndex = 2;
                return 2;
            }
            else if (minScoreP == 6)
            {
                //readerconfig.modeIndex = 3;
                return 3;
            }
            else {
                return 3;
            }

        }

        public string GetReaderConfigMode()
        {
            return readerconfig.modeIndex.ToString();
        }

        // This guy checks to see if cmd is different than last time before executing.
        public void RestartRead(ReadCmdSettings cmd)
        {
            if (!IsConnected())
            {
                gui.AppendToDebugTextBox("Can't start read: Reader not connected.");
                return;
            }

            if (IsReadRunning() && !cmd.Equals(readcmdsettings))
            {
                StopRead();
                StartRead(cmd);
            }
            else if (!IsReadRunning()) // idiot proof
            {
                StartRead(cmd);
            }
        }

        public void StartRead(ReadCmdSettings cmd)
        {
            if(IsReadRunning())
            {
                gui.AppendToDebugTextBox("Read already configured - use ReStart Read");
            }
            else if(!IsConnected())
            {
                gui.AppendToDebugTextBox("Can't start read: Reader not connected.");
            }
            else
            {
                //AddReadAccessSpec("0D00", "0000000000000000", 1, 0);
                reader.AddReadAccessSpec(cmd.tagID, cmd.mask, cmd.wordCount, cmd.wordPtr, readerconfig);
                reader.ENABLE_ACCESSSPEC();
                readcmdsettings = cmd;
                readConfigured = true;
            }

            /*
            Set_Reader_Config();   // Sets the client configuration
            readmode = true;   // this flag is used for special settings in the Add_RoSpec method

            // stop all Specs
            Delete_RoSpec();
            DELETE_ACCESSSPEC();

            // add new roSpec and accessSpec
            AddReadAccessSpec("0D00", "0000000000000000", 1, 1);
            Add_RoSpec();

            // enable new accessSpec and RoSpec
            ENABLE_ACCESSSPEC();
            Enable_RoSpec();
            */
        }

        public void StopInventory()
        {
            if (IsInventoryRunning())
            {
                stopInventory = true;
                reader.Stop_RoSpec();
                reader.Delete_RoSpec();
                currentMode = GuiModes.Ready;
                if (probe_channel_timer != null) {
                    probe_channel_timer.Close();
                }
            }
        }

        public void StopRead()
        {
            if (IsReadRunning())
            {
                //Stop_RoSpec();
                //Delete_RoSpec();
                reader.DELETE_ACCESSSPEC();
                readConfigured = false;
            }
        }

        public bool IsInventoryRunning()
        {
            return (currentMode == GuiModes.UserInventory);
        }

        public bool IsReadRunning()
        {
            return readConfigured;
        }

        public bool IsConnected()
        {
            return (currentMode != GuiModes.Idle);
        }


        public ReaderConfig getReaderConfig()
        {
            return readerconfig;
        }
        
        public InventoryConfig getInventoryConfig()
        {
            return inventoryconfig;
        }

        public ReaderMode[] GetReaderModulationModes()
        {
            return readerModes;
        }

        public ReaderMode FindMode(uint modeIdentifier)
        {
            for (int i = 0; i < readerModes.Length; i++)
                if (readerModes[i].GetModeIdentifier() == modeIdentifier)
                    return readerModes[i];
            return null;
        }


        #endregion

        #region Data Structures


        public struct ReaderConfig
        {
            public ushort attenuation;
            public ushort modeIndex;
            public ushort tagPopulation;
            public ushort tagTransitTime;
            public bool[] antennaID;
            public ushort readerSensitivity;
            public ushort channelIndex;
            public ushort hopTableIndex;
            public ushort periodicTriggerValue;
        }

        public struct InventoryConfig
        {
            public LLRP.ENUM_ROSpecStartTriggerType startTrigger;
            public LLRP.ENUM_ROSpecStopTriggerType stopTrigger;
            public LLRP.ENUM_AISpecStopTriggerType AITriggerType;
            public ushort duration;
            public ushort numAttempts;
            public ushort numTags;
            public LLRP.ENUM_ROReportTriggerType reportTrigger;
            public ushort reportN;
            public uint AITimeout;
        }


        public void SetDefaultReaderConfig()
        {
            readerconfig.attenuation = 0;
            readerconfig.modeIndex = 2;
            readerconfig.tagPopulation = 1;
            readerconfig.tagTransitTime = 0;
            readerconfig.antennaID = new bool[] { true, false, false, false };
            readerconfig.readerSensitivity = 1;
            readerconfig.channelIndex = 0;
            readerconfig.hopTableIndex = 1;
            readerconfig.periodicTriggerValue = 0;
        }

        public void setReaderConfig(ReaderConfig config)
        {
            readerconfig.attenuation = config.attenuation;
            readerconfig.modeIndex = config.modeIndex;
            readerconfig.tagPopulation = config.tagPopulation;
            readerconfig.tagTransitTime = config.tagTransitTime;
            readerconfig.antennaID = config.antennaID;
            readerconfig.readerSensitivity = config.readerSensitivity;
            readerconfig.channelIndex = config.channelIndex;
            readerconfig.hopTableIndex = config.hopTableIndex;
            readerconfig.periodicTriggerValue = config.periodicTriggerValue;
        }

        public void SetDefaultInventoryConfig()
        {
            inventoryconfig.startTrigger = ENUM_ROSpecStartTriggerType.Immediate;
            inventoryconfig.stopTrigger = ENUM_ROSpecStopTriggerType.Null;
            inventoryconfig.AITriggerType = ENUM_AISpecStopTriggerType.Duration;
            inventoryconfig.duration = 100;
            inventoryconfig.numAttempts = 1;
            inventoryconfig.numTags = 1;
            inventoryconfig.reportTrigger = ENUM_ROReportTriggerType.Upon_N_Tags_Or_End_Of_AISpec;
            inventoryconfig.reportN = 1;
            inventoryconfig.AITimeout = 0;
        }

        public void setInventoryConfig(InventoryConfig config)
        {
            inventoryconfig.startTrigger = config.startTrigger;
            inventoryconfig.stopTrigger = config.stopTrigger;
            inventoryconfig.AITriggerType = config.AITriggerType;
            inventoryconfig.duration = config.duration;
            inventoryconfig.numAttempts = config.numAttempts;
            inventoryconfig.numTags = config.numTags;
            inventoryconfig.reportTrigger = config.reportTrigger;
            inventoryconfig.reportN = config.reportN;
            inventoryconfig.AITimeout = config.AITimeout;
        }

        public class ReaderMode
        {
            uint modeIndentifier;
            ENUM_C1G2DRValue dr;
            ENUM_C1G2MValue m;
            uint tagRate;
            ENUM_C1G2ForwardLinkModulation linkModulation;
            uint pie;
            uint minTari;
            uint maxTari;

            public ReaderMode(PARAM_C1G2UHFRFModeTableEntry mode)
            {
                modeIndentifier = mode.ModeIdentifier;
                dr = mode.DRValue;
                m = mode.MValue;
                tagRate = mode.BDRValue;
                linkModulation = mode.ForwardLinkModulation;
                pie = mode.PIEValue;
                minTari = mode.MinTariValue;
                maxTari = mode.MaxTariValue;
            }

            public uint GetModeIdentifier()
            {
                return modeIndentifier;
            }

            override public string ToString()
            {
                const int stop1 = 13;
                const int stop2 = stop1 + 8;
                const int stop3 = stop2 + 15;

                // Mode Identifier
                string outstr = "[" + modeIndentifier.ToString() + "]";
                outstr = PadSpaces(stop1, outstr);
                // Miller
                switch (m)
                {
                    case ENUM_C1G2MValue.MV_FM0:
                        outstr += "FM0";
                        break;
                    case ENUM_C1G2MValue.MV_2:
                        outstr += "M2";
                        break;
                    case ENUM_C1G2MValue.MV_4:
                        outstr += "M4";
                        break;
                    case ENUM_C1G2MValue.MV_8:
                        outstr += "M8";
                        break;

                }
                outstr = PadSpaces(stop2, outstr);

                // Divide Ratio
                switch (dr)
                {
                    case ENUM_C1G2DRValue.DRV_8:
                        outstr += "DR 8";
                        break;
                    case ENUM_C1G2DRValue.DRV_64_3:
                        outstr += "DR 64/3";
                        break;
                }

                outstr = PadSpaces(stop3, outstr);

                // Tari
                outstr += "Tari = " + ((double)(minTari / 1000.0)).ToString() + " us";

                return outstr;
            }
            private string PadSpaces(int desiredTotalLength, string input)
            {
                for (int i = input.Length; i < desiredTotalLength; i++)
                    input += " ";
                return input;
            }
        }

        public struct ReadCmdSettings
        {
            public string tagID;
            public string mask;
            public ushort wordPtr;
            public ushort wordCount;
            public ReadCmdSettings(string tagID, string mask, ushort wordCount, ushort wordPtr)
            {
                this.tagID = tagID;
                this.mask = mask;
                this.wordPtr = wordPtr;
                this.wordCount = wordCount;
            }
            public override bool Equals(object obj)
            {
                if (obj.GetType().Name != "ReadCmdSettings") return false;
                ReadCmdSettings compareTo = (ReadCmdSettings)obj;
                if (compareTo.tagID != this.tagID) return false;
                if (compareTo.mask != this.mask) return false;
                if (compareTo.wordPtr != this.wordPtr) return false;
                if (compareTo.wordCount != this.wordCount) return false;
                return true;
            }
            public override int GetHashCode()
            {
                return base.GetHashCode();
            }
        }

        #endregion

        #region Printing and Debugging Methods

        private void WriteMessage(string rsp)
        {
            WriteMessage(rsp, null);
        }

        public void WriteMessage(string rsp, string source)
        {
            // Clear textbox
            //textBox2.clear();
            //gui.AppendToDebugTextBox("\r\n===================================\r\n");
            gui.AppendToDebugTextBox(source + "\r\n");

            // let user know operation completed
            gui.AppendToDebugTextBox(parseXML(rsp));
            gui.AppendToDebugTextBox("===================================\r\n");
        }

        public void WriteString(string info)
        {
            gui.AppendToDebugTextBox(info);
            gui.AppendToDebugTextBox("===================================\r\n");
        }

        private string parseXML(string msg)
        {
            // divide XML message into chunks
            string str = msg.ToString();
            string[] strArr = str.Split(new string[] { "><" }, StringSplitOptions.None);
            string message = null;

            // parse relevant string
            for (int i = 0; i < strArr.Length; i++)
            {
                if (strArr[i].Contains("</"))
                {
                    string[] temp = strArr[i].Split(new char[] { '>' });
                    message = message + temp[0] + " = " + (temp[1].Split(new string[] { "</" }, StringSplitOptions.None))[0] + "\r\n";
                }
            }

            return message;
        }

        #endregion

    }
}
