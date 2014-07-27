﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Serialization;
using System.Windows.Forms;

using ConfigurationSettings;
using Configuration.DkimSigner.Exchange;
using Configuration.DkimSigner.GitHub;
using Heijden.DNS;

namespace Configuration.DkimSigner
{
    public partial class MainWindow : Form
    {
        /**********************************************************/
        /*********************** Variables ************************/
        /**********************************************************/

        private ExchangeServer oExchange = null;
        private Settings oConfig = null;
        private Release dkimSignerAvailable = null;
        private Version dkimSignerInstalled = null;

        private bool bDataUpdated = false;
        
        private Thread thDkimSignerInstalled = null;
        private Thread thDkimSignerAvailable = null;
        private Thread thTransportServiceOperation = null;
        private System.Threading.Timer tiTransportServiceStatus = null;

        delegate void SetDkimSignerInstalledCallback();
        delegate void SetDkimSignerAvailableCallback();
        delegate void SetExchangeTransportServiceStatusCallback();

        /**********************************************************/
        /*********************** Construtor ***********************/
        /**********************************************************/

        public MainWindow(ExchangeServer oServer)
        {           
            this.InitializeComponent();

            this.cbLogLevel.SelectedItem = "Information";
            this.cbKeyLength.SelectedItem = "1024";

            this.oExchange = oServer;
        }

        /**********************************************************/
        /************************* Events *************************/
        /**********************************************************/

        /// <summary>
        /// Load information in the Windowform
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Load(object sender, EventArgs e)
        {
            // Get Exchange version installed + load the current configuration
            this.txtExchangeInstalled.Text = oExchange.GetInstalledVersion();

            // Uptade Microsft Exchange Transport Service stuatus
            if (this.txtExchangeInstalled.Text != "Not installed")
            {
                this.tiTransportServiceStatus = new System.Threading.Timer(new TimerCallback(this.CheckExchangeTransportServiceStatus), null, 0, 1000);
                this.btConfigureTransportService.Enabled = true;
            }
            
            // Update Exchange and DKIM Signer version
            this.UpdateVersions();

            // Load setting from XML file
            this.LoadDkimSignerConfig();
        }

        /// <summary>
        /// Confirm the configuration saving before quit the application
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            // IF any thread running, we stop them before exit
            if (this.thDkimSignerAvailable != null && this.thDkimSignerAvailable.ThreadState == ThreadState.Running)
            {
                this.thDkimSignerAvailable.Abort();
            }

            if (this.thDkimSignerInstalled != null && this.thDkimSignerInstalled.ThreadState == ThreadState.Running)
            {
                this.thDkimSignerInstalled.Abort();
            }

            // Check if the config have been change and haven't been save
            if (!this.CheckSaveConfig())
            {
                e.Cancel = true;
            }
        }

        private void txtExchangeStatus_TextChanged(object sender, System.EventArgs e)
        {
            switch (this.txtExchangeStatus.Text)
            {
                case "Running":
                    this.btStartTransportService.Enabled = false;
                    this.btStopTransportService.Enabled = true;
                    this.btRestartTransportService.Enabled = true;
                    break;

                case "Stopped":
                    this.btStartTransportService.Enabled = true;
                    this.btStopTransportService.Enabled = false;
                    this.btRestartTransportService.Enabled = false;
                    break;
                default:
                    this.btStartTransportService.Enabled = false;
                    this.btStopTransportService.Enabled = false;
                    this.btRestartTransportService.Enabled = false;
                    break;
            }
        }

        private void cbxPrereleases_CheckedChanged(object sender, EventArgs e)
        {
            this.UpdateVersions();
        }

        private void rbRsaSha1_CheckedChanged(object sender, System.EventArgs e)
        {
            this.bDataUpdated = true;
        }

        private void rbRsaSha256_CheckedChanged(object sender, System.EventArgs e)
        {
            this.bDataUpdated = true;
        }

        private void rbSimpleHeaderCanonicalization_CheckedChanged(object sender, System.EventArgs e)
        {
            this.bDataUpdated = true;
        }

        private void rbRelaxedHeaderCanonicalization_CheckedChanged(object sender, System.EventArgs e)
        {
            this.bDataUpdated = true;
        }

        private void rbSimpleBodyCanonicalization_CheckedChanged(object sender, System.EventArgs e)
        {
            this.bDataUpdated = true;
        }

        private void rbRelaxedBodyCanonicalization_CheckedChanged(object sender, System.EventArgs e)
        {
            this.bDataUpdated = true;
        }

        private void cbLogLevel_TextChanged(object sender, System.EventArgs e)
        {
            this.bDataUpdated = true;
        }

        private void lbxHeadersToSign_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.btHeaderDelete.Enabled = (this.lbxHeadersToSign.SelectedItem != null);
        }

        private void lbxDomains_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.lbxDomains.SelectedItems.Count == 0)
            {
                this.txtDomainName.Text = "";
                this.txtDomainSelector.Text = "";
                this.txtDomainPrivateKeyFilename.Text = "";
                this.txtDomainDNS.Text = "";
                this.gbxDomainDetails.Enabled = false;
            }
            else
            {
                DomainElement oSelected = (DomainElement) this.lbxDomains.SelectedItem;
                this.txtDomainName.Text = oSelected.Domain;
                this.txtDomainSelector.Text = oSelected.Selector;
                this.txtDomainPrivateKeyFilename.Text = oSelected.PrivateKeyFile;

                this.UpdateSuggestedDNS();
                this.txtDomainDNS.Text = "";
                this.gbxDomainDetails.Enabled = true;
                this.btDomainDelete.Enabled = true;
                this.btDomainSave.Enabled = false;

                this.bDataUpdated = false;
            }
        }

        private void txtDomainName_TextChanged(object sender, EventArgs e)
        {           
            if (Uri.CheckHostName(this.txtDomainName.Text) != UriHostNameType.Dns)
            {
                this.epvDomainSelector.SetError(this.txtDomainName, "Invalid DNS name. Format: 'example.com'");
            }
            else
            {
                this.epvDomainSelector.SetError(this.txtDomainName, null);
            }

            this.btDomainSave.Enabled = true;
            this.txtDNSName.Text = this.txtDomainSelector.Text + "._domainkey." + this.txtDomainName.Text + ".";

            this.bDataUpdated = true;
        }

        private void txtDomainSelector_TextChanged(object sender, EventArgs e)
        {
            if (!Regex.IsMatch(this.txtDomainSelector.Text, @"^[a-zA-Z0-9_]+$", RegexOptions.None))
            {
                this.epvDomainSelector.SetError(this.txtDomainSelector, "The selector should only contain characters, numbers and underscores.");
            }
            else
            {
                this.epvDomainSelector.SetError(this.txtDomainSelector, null);
            }

            this.btDomainSave.Enabled = true;
            this.txtDNSName.Text = this.txtDomainSelector.Text + "._domainkey." + this.txtDomainName.Text + ".";

            this.bDataUpdated = true;
        }

        /**********************************************************/
        /******************* Internal functions *******************/
        /**********************************************************/

        /// <summary>
        /// Check if a string is in base64
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static bool IsBase64String(string s)
        {
            s = s.Trim();
            return (s.Length % 4 == 0) && Regex.IsMatch(s, @"^[a-zA-Z0-9\+/]*={0,3}$", RegexOptions.None);
        }

        /// <summary>
        /// 
        /// </summary>
        private void SetExchangeTransportServiceStatus()
        {
            string sStatus = null;

            try
            {
                sStatus = oExchange.GetTransportServiceStatus().ToString();
            }
            catch (ExchangeHelperException)
            {
                this.tiTransportServiceStatus.Change(Timeout.Infinite, Timeout.Infinite);
            }

            this.txtExchangeStatus.Text = (sStatus != null ? sStatus : "Unknown");
        }

        /// <summary>
        /// Set the value of txtDkimSignerInstalled from CheckDkimSignerInstalledSafe (use by thread DkimSignerInstalled)
        /// </summary>
        /// <param name="dkimSignerInstalled"></param>
        private void SetDkimSignerInstalled()
        {
            this.txtDkimSignerInstalled.Text = (this.dkimSignerInstalled != null ? this.dkimSignerInstalled.ToString() : "Not installed");
        }

        /// <summary>
        ///  Set the value of txtDkimSignerAvailable from CheckDkimSignerAvailableSafe (use by thread DkimSignerAvailable)
        /// </summary>
        /// <param name="dkimSignerAvailable"></param>
        private void SetDkimSignerAvailable()
        {
            if (this.dkimSignerAvailable != null)
            {
                string version = this.dkimSignerAvailable.Version.ToString();

                Match match = Regex.Match(this.dkimSignerAvailable.TagName, @"v?((?:\d+\.){0,3}\d+)(?:-(alpha|beta)(?:\.(\d+))?)?", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    if (match.Groups.Count > 2 && match.Groups[2].Value.Length > 0)
                    {
                        version += " (" + match.Groups[2].Value;
                        if (match.Groups.Count > 3 && match.Groups[3].Value.Length > 0)
                        {
                            version += "." + match.Groups[3].Value; 
                        }
                        version += ")";
                    }
                }

                this.txtDkimSignerAvailable.Text = version;
                this.txtChangelog.Text = this.dkimSignerAvailable.Body;
            }
            else
            {
                this.txtDkimSignerAvailable.Text = "Unknown";
                this.txtChangelog.Text = "Couldn't get current version.\r\nCheck your Internet connection or restart the application.";
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="state"></param>
        private void CheckExchangeTransportServiceStatus(object state)
        {
            if (this.txtExchangeStatus.InvokeRequired)
            {
                SetExchangeTransportServiceStatusCallback d = new SetExchangeTransportServiceStatusCallback(SetExchangeTransportServiceStatus);
                this.Invoke(d);
            }
            else
            {
                this.SetExchangeTransportServiceStatus();
            }
        }

        /// <summary>
        /// Thread safe function for the thread DkimSignerInstalled
        /// </summary>
        private void CheckDkimSignerInstalledSafe()
        {
            // Check if DKIM Agent is in C:\Program Files\Exchange DkimSigner and get version of DLL
            try
            {
                this.dkimSignerInstalled = Version.Parse(System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(Constants.DKIM_SIGNER_PATH, Constants.DKIM_SIGNER_AGENT_DLL)).ProductVersion);
            }
            catch (Exception)
            {
                this.dkimSignerInstalled = null;
            }

            // Check if DKIM agent have been load in Exchange
            if (this.dkimSignerInstalled != null)
            {
                try
                {
                    if (!oExchange.IsDkimAgentTransportInstalled())
                    {
                        this.dkimSignerInstalled = null;
                    }
                }
                catch (Exception) { }
            }

            // Set the result in the textbox
            if (this.txtDkimSignerInstalled.InvokeRequired)
            {
                SetDkimSignerInstalledCallback d = new SetDkimSignerInstalledCallback(SetDkimSignerInstalled);
                this.Invoke(d);
            }
            else
            {
                this.SetDkimSignerInstalled();
            }
        }

        /// <summary>
        /// Thread safe function for the thread DkimSignerAvailable
        /// </summary>
        private void CheckDkimSignerAvailableSafe()
        {
            // Check the lastest Release
            try
            {
                this.dkimSignerAvailable = ApiWrapper.getNewestRelease(cbxPrereleases.Checked);
            }
            catch (Exception)
            {
                this.dkimSignerAvailable = null;
            }

            // Set the result in the textbox
            if (this.txtDkimSignerInstalled.InvokeRequired)
            {
                SetDkimSignerAvailableCallback d = new SetDkimSignerAvailableCallback(SetDkimSignerAvailable);
                this.Invoke(d);
            }
            else
            {
                this.SetDkimSignerAvailable();
            }
        }

        private void StartTransportServiceSafe()
        {
            try
            {
                this.oExchange.StartTransportService();

                MessageBox.Show("MSExchangeTransport service have been successfully started.\n", "Service information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (ExchangeHelperException)
            {
                MessageBox.Show("Couldn't change MSExchangeTransport service status.\n", "Service error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopTransportServiceSafe()
        {
            try
            {
                this.oExchange.StopTransportService();

                MessageBox.Show("MSExchangeTransport service have been successfully stopped.\n", "Service information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (ExchangeHelperException)
            {
                MessageBox.Show("Couldn't change MSExchangeTransport service status.\n", "Service error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private void RestartTransportServiceSafe()
        {
            try
            {
                this.oExchange.RestartTransportService();

                MessageBox.Show("MSExchangeTransport service have been successfully restarted.\n", "Service information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (ExchangeHelperException)
            {
                MessageBox.Show("Couldn't change MSExchangeTransport service status.\n", "Service error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Update the current available and installed version info
        /// </summary>
        private void UpdateVersions()
        {
            // Get Exchange.DkimSigner version installed
            this.txtDkimSignerInstalled.Text = "Loading ...";
            this.thDkimSignerInstalled = new Thread(new ThreadStart(this.CheckDkimSignerInstalledSafe));

            // Get Exchange.DkimSigner version available
            this.txtDkimSignerAvailable.Text = "Loading ...";
            this.thDkimSignerAvailable = new Thread(new ThreadStart(this.CheckDkimSignerAvailableSafe));

            // Start the threads that will do lookup
            try
            {
                this.thDkimSignerInstalled.Start();
                this.thDkimSignerAvailable.Start();
            }
            catch (ThreadAbortException) { }
        }

        /// <summary>
        /// Asks the user if he wants to save the current config and saves it.
        /// </summary>
        /// <returns>false if the user pressed cancel. true otherwise</returns>
        private bool CheckSaveConfig()
        {
            bool bStatus = true;

            // IF the configuration have changed
            if (this.bDataUpdated)
            {
                DialogResult result = MessageBox.Show("Do you want to save your changes?", "Save changes?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                // IF we want to save the change
                if (result == DialogResult.Yes)
                {
                    // IF we can't save the changes
                    if (!this.SaveDkimSignerConfig())
                    {
                        if (MessageBox.Show("Error saving config. Do you wan to close anyways? This will discard all the changes!", "Discard changes?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                        {
                            bStatus = false;
                        }
                    }
                }
                // IF we cancel the save of our changes
                else if (result == DialogResult.Cancel)
                {
                    bStatus = false;
                }
            }

            return bStatus;
        }

        /// <summary>
        /// Load the current configuration for Exchange DkimSigner from the registry
        /// </summary>
        private void LoadDkimSignerConfig()
        {
            try
            {
                this.oConfig = Settings.LoadOrCreate(Path.Combine(Constants.DKIM_SIGNER_PATH, "settings.xml"));

                switch (this.oConfig.Loglevel)
                {
                    case 1:
                        this.cbLogLevel.Text = "Error";
                        break;
                    case 2:
                        this.cbLogLevel.Text = "Warning";
                        break;
                    case 3:
                        this.cbLogLevel.Text = "Information";
                        break;
                    default:
                        this.cbLogLevel.Text = "Information";
                        MessageBox.Show("The log level is invalid. The log level have been set to Information.");
                        break;
                }

                this.rbRsaSha1.Checked = (oConfig.SigningAlgorithm == DkimAlgorithmKind.RsaSha1);

                this.rbSimpleHeaderCanonicalization.Checked = (this.oConfig.HeaderCanonicalization == DkimCanonicalizationKind.Simple);
                this.rbRelaxedHeaderCanonicalization.Checked = (this.oConfig.HeaderCanonicalization == DkimCanonicalizationKind.Relaxed);
                this.rbSimpleBodyCanonicalization.Checked = (this.oConfig.BodyCanonicalization == DkimCanonicalizationKind.Simple);
                this.rbRelaxedBodyCanonicalization.Checked = (this.oConfig.BodyCanonicalization == DkimCanonicalizationKind.Relaxed);

                this.lbxHeadersToSign.Items.Clear();

                foreach (string sItem in this.oConfig.HeadersToSign)
                {
                    this.lbxHeadersToSign.Items.Add(sItem);
                }

                this.lbxHeadersToSign.SelectedItem = null;

                this.ReloadDomainsList();

                this.bDataUpdated = false;
            }
            catch (Exception e)
            {
                MessageBox.Show("Couldn't load the settings file:\n" + e.Message + "\nSetting it to default values.", "Settings error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Save the new configuration into registry for Exchange DkimSigner
        /// </summary>
        private bool SaveDkimSignerConfig()
        {
            this.oConfig.Loglevel = this.cbLogLevel.SelectedIndex + 1;

            this.oConfig.SigningAlgorithm = (this.rbRsaSha1.Checked ? DkimAlgorithmKind.RsaSha1 : DkimAlgorithmKind.RsaSha256);
            this.oConfig.BodyCanonicalization = (this.rbSimpleBodyCanonicalization.Checked ? DkimCanonicalizationKind.Simple : DkimCanonicalizationKind.Relaxed);
            this.oConfig.HeaderCanonicalization = (this.rbSimpleHeaderCanonicalization.Checked ? DkimCanonicalizationKind.Simple : DkimCanonicalizationKind.Relaxed);

            this.oConfig.HeadersToSign.Clear();

            foreach (string sItem in this.lbxHeadersToSign.Items)
            {
                this.oConfig.HeadersToSign.Add(sItem);
            }

            this.oConfig.Save(Path.Combine(Constants.DKIM_SIGNER_PATH, "settings.xml"));

            this.bDataUpdated = false;

            return true;
        }

        /// <summary>
        /// Reload the list of domain from the configuration file
        /// </summary>
        /// <param name="selectedDomain"></param>
        private void ReloadDomainsList(string selectedDomain = null)
        {
            DomainElement oCurrentDomain = null;

            if (this.lbxDomains.SelectedItem != null)
            {
                oCurrentDomain = (DomainElement) this.lbxDomains.SelectedItem;
            }

            this.lbxDomains.Items.Clear();
            
            foreach (DomainElement oConfigDomain in this.oConfig.Domains)
            {
                this.lbxDomains.Items.Add(oConfigDomain);
            }
            
            if (oCurrentDomain != null)
            {
                this.lbxDomains.SelectedItem = oCurrentDomain;
            }
        }

                //
                // TODO : Problem when sRsaPublicKeyBase64 is empty
                //
        private void UpdateSuggestedDNS(string sRsaPublicKeyBase64 = "")
        {
            string sDNSRecord = "v=DKIM1; k=rsa; p=" + sRsaPublicKeyBase64;

            if (sRsaPublicKeyBase64 == string.Empty)
            {
                string sPubKeyPath = this.txtDomainPrivateKeyFilename.Text;

                if (!Path.IsPathRooted(sPubKeyPath))
                {
                    sPubKeyPath = Path.Combine(Constants.DKIM_SIGNER_PATH, "keys", sPubKeyPath);
                }

                sPubKeyPath += ".pub";
                if (File.Exists(sPubKeyPath))
                {
                    string[] asContents = File.ReadAllLines(sPubKeyPath);

                    if (asContents.Length > 2 && asContents[0].Equals("-----BEGIN PUBLIC KEY-----") && IsBase64String(asContents[1]))
                    {
                        sRsaPublicKeyBase64 = asContents[1];
                    }
                    else
                    {
                        sDNSRecord = "No valid RSA pub key:\n" + sPubKeyPath;
                    }
                }
                else
                {
                    sDNSRecord = "No RSA pub key found:\n" + sPubKeyPath;
                }
            }

            this.txtDNSRecord.Text = sDNSRecord;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sPath"></param>
        private void SetDomainKeyPath(string sPath)
        {
            string sKeyDir = Path.Combine(Constants.DKIM_SIGNER_PATH, "keys");

            if (sPath.StartsWith(sKeyDir))
            {
                sPath = sPath.Substring(sKeyDir.Length + 1);
            }
            else if (MessageBox.Show("It is strongly recommended to store all the keys in the directory\n" + sKeyDir + "\nDo you want me to move the key into this directory?", "Move key?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                List<string> asFile = new List<string>();
                asFile.Add(sPath);
                asFile.Add(sPath + ".pub");
                asFile.Add(sPath + ".pem");

                foreach (string sFile in asFile)
                {
                    if (File.Exists(sFile))
                    {
                        string sFilename = Path.GetFileName(sFile);
                        string sNewPath = Path.Combine(sKeyDir, sFilename);
                        try
                        {
                            File.Move(sFile, sNewPath);
                            sPath = sNewPath.Substring(sKeyDir.Length - 4);
                        }
                        catch (IOException ex)
                        {
                            MessageBox.Show("Couldn't move file:\n" + sFile + "\nto\n" + sNewPath + "\n" + ex.Message, "Error moving file", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }

            this.txtDomainPrivateKeyFilename.Text = sPath;
            this.btDomainSave.Enabled = true;

            this.bDataUpdated = true;
        }

        /**********************************************************/
        /********************** Button click **********************/
        /**********************************************************/

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btStartTransportService_Click(object sender, EventArgs e)
        {
            this.thTransportServiceOperation = new Thread(new ThreadStart(this.StartTransportServiceSafe));

            try
            {
                this.thTransportServiceOperation.Start();
            }
            catch (ThreadAbortException) { }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btStopTransportService_Click(object sender, EventArgs e)
        {
            this.thTransportServiceOperation = new Thread(new ThreadStart(this.StopTransportServiceSafe));

            try
            {
                this.thTransportServiceOperation.Start();
            }
            catch (ThreadAbortException) { }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btRestartTransportService_Click(object sender, EventArgs e)
        {
            this.thTransportServiceOperation = new Thread(new ThreadStart(this.RestartTransportServiceSafe));

            try
            {
                this.thTransportServiceOperation.Start();
            }
            catch (ThreadAbortException) { }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btConfigureTransportService_Click(object sender, EventArgs e)
        {
            ExchangeTransportServiceWindows oEtsw = new ExchangeTransportServiceWindows(this.oExchange);

            oEtsw.ShowDialog();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btHeaderAdd_Click(object sender, EventArgs e)
        {
            HeaderInputWindows oHiw = new HeaderInputWindows();

            if (oHiw.ShowDialog() == DialogResult.OK)
            {
                this.lbxHeadersToSign.Items.Add(oHiw.txtHeader.Text);
                this.lbxHeadersToSign.SelectedItem = oHiw.txtHeader;

                this.bDataUpdated = true;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btHeaderDelete_Click(object sender, EventArgs e)
        {
            if (this.lbxHeadersToSign.SelectedItem != null)
            {
                this.lbxHeadersToSign.Items.Remove(lbxHeadersToSign.SelectedItem);

                this.bDataUpdated = true;
            }
        }

        /// <summary>
        /// Button "Save configuration" action
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btSaveConfiguration_Click(object sender, EventArgs e)
        {
            this.SaveDkimSignerConfig();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btAddDomain_Click(object sender, EventArgs e)
        {
            if (this.bDataUpdated)
            {
                DialogResult result = MessageBox.Show("Do you want to save the current changes?", "Save changes?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    if (!SaveDkimSignerConfig())
                        return;
                }

                if (result == DialogResult.Cancel)
                {
                    return;
                }
            }

            this.lbxDomains.ClearSelected();
            this.txtDNSRecord.Text = "";
            this.txtDNSName.Text = "";
            this.txtDNSRecord.Text = "";
            this.gbxDomainDetails.Enabled = true;
            this.btDomainDelete.Enabled = false;

            this.bDataUpdated = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btDomainDelete_Click(object sender, EventArgs e)
        {
            if (this.lbxDomains.SelectedItem != null)
            {
                DomainElement oCurrentDomain = (DomainElement)this.lbxDomains.SelectedItem;
                this.oConfig.Domains.Remove(oCurrentDomain);

                this.lbxDomains.Items.Remove(oCurrentDomain);
                this.lbxDomains.SelectedItem = null;
            }

            string keyFile = Path.Combine(Constants.DKIM_SIGNER_PATH, "keys", this.txtDomainPrivateKeyFilename.Text);

            List<string> asFile = new List<string>();
            asFile.Add(keyFile);
            asFile.Add(keyFile + ".pub");
            asFile.Add(keyFile + ".pem");

            foreach (string sFile in asFile)
            {
                if (File.Exists(sFile) && MessageBox.Show("Do you want me to delete the key file?\n" + sFile, "Delete key?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try
                    {
                        File.Delete(sFile);
                    }
                    catch (IOException ex)
                    {
                        MessageBox.Show("Couldn't delete file:\n" + sFile + "\n" + ex.Message, "Error deleting file", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            this.SaveDkimSignerConfig();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btDomainKeyGenerate_Click(object sender, EventArgs e)
        {
            SaveFileDialog oFileDialog = new SaveFileDialog();

            oFileDialog.DefaultExt = "xml";
            oFileDialog.Filter = "All files|*.*";
            oFileDialog.Title = "Select a location for the new key file";

            oFileDialog.InitialDirectory = Path.Combine(Constants.DKIM_SIGNER_PATH, "keys");

            if (!Directory.Exists(oFileDialog.InitialDirectory))
            {
                Directory.CreateDirectory(oFileDialog.InitialDirectory);
            }

            if (this.txtDomainName.Text.Length > 0)
            {
                oFileDialog.FileName = this.txtDomainName.Text + ".xml";
            }

            if (oFileDialog.ShowDialog() == DialogResult.OK)
            {
                RSACryptoServiceProvider oProvider = new RSACryptoServiceProvider(Convert.ToInt32(this.cbKeyLength.Text, 10));
                CSInteropKeys.AsnKeyBuilder.AsnMessage oPublicEncoded = CSInteropKeys.AsnKeyBuilder.PublicKeyToX509(oProvider.ExportParameters(true));
                CSInteropKeys.AsnKeyBuilder.AsnMessage oPrivateEncoded = CSInteropKeys.AsnKeyBuilder.PrivateKeyToPKCS8(oProvider.ExportParameters(true));

                File.WriteAllBytes(oFileDialog.FileName, Encoding.ASCII.GetBytes(oProvider.ToXmlString(true)));
                File.WriteAllText(oFileDialog.FileName + ".pub", "-----BEGIN PUBLIC KEY-----\r\n" + Convert.ToBase64String(oPublicEncoded.GetBytes()) + "\r\n-----END PUBLIC KEY-----");
                File.WriteAllText(oFileDialog.FileName + ".pem", "-----BEGIN RSA PRIVATE KEY-----\r\n" + Convert.ToBase64String(oPrivateEncoded.GetBytes()) + "\r\n-----END RSA PRIVATE KEY-----");

                this.UpdateSuggestedDNS(Convert.ToBase64String(oPublicEncoded.GetBytes()));
                this.SetDomainKeyPath(oFileDialog.FileName);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btDomainKeySelect_Click(object sender, EventArgs e)
        {
            OpenFileDialog oFileDialog = new OpenFileDialog();

            oFileDialog.FileName = "key";
            oFileDialog.Filter = "Key files|*.xml;*.pem|All files|*.*";
            oFileDialog.Title = "Select a private key for signing";
            oFileDialog.InitialDirectory = Path.Combine(Constants.DKIM_SIGNER_PATH, "keys");

            if (oFileDialog.ShowDialog() == DialogResult.OK)
            {
                this.SetDomainKeyPath(oFileDialog.FileName);
                this.UpdateSuggestedDNS();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btDomainCheckDNS_Click(object sender, EventArgs e)
        {
            string sFullDomain = this.txtDomainSelector.Text + "._domainkey." + this.txtDomainName.Text;

            try
            {
                Resolver oResolver = new Resolver();
                oResolver.Recursion = true;
                oResolver.UseCache = false;

                // Get the name server for the domain to avoid DNS caching
                Response oResponse = oResolver.Query(sFullDomain, QType.NS, QClass.IN);
                if (oResponse.RecordsRR.GetLength(0) > 0)
                {
                    RR oNsRecord = oResponse.RecordsRR[0];
                    if (oNsRecord.RECORD.RR.RECORD.GetType() == typeof(RecordSOA))
                    {
                        RecordSOA oSoaRecord = (RecordSOA)oNsRecord.RECORD.RR.RECORD;
                        oResolver.DnsServer = oSoaRecord.MNAME;
                    }
                }

                // Get the TXT record for DKIM
                oResponse = oResolver.Query(sFullDomain, QType.TXT, QClass.IN);
                if (oResponse.RecordsTXT.GetLength(0) > 0)
                {
                    RecordTXT oTxtRecord = oResponse.RecordsTXT[0];
                    this.txtDomainDNS.Text = oTxtRecord.TXT.Count > 0 ? oTxtRecord.TXT[0] : "No record found for " + sFullDomain;
                }
                else
                {
                    this.txtDomainDNS.Text = "No record found for " + sFullDomain;
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show("Coldn't get DNS record:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.txtDomainDNS.Text = "Error getting record.";
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btDomainSave_Click(object sender, EventArgs e)
        {
            if (this.epvDomainSelector.GetError(txtDomainName) == "" && this.epvDomainSelector.GetError(txtDomainSelector) == "")
            {
                DomainElement oCurrentDomain;
                bool bAddToList = false;

                if (this.lbxDomains.SelectedItem != null)
                {
                    oCurrentDomain = (DomainElement)this.lbxDomains.SelectedItem;
                }
                else
                {
                    oCurrentDomain = new DomainElement();
                    bAddToList = true;
                }

                oCurrentDomain.Domain = this.txtDomainName.Text;
                oCurrentDomain.Selector = this.txtDomainSelector.Text;
                oCurrentDomain.PrivateKeyFile = this.txtDomainPrivateKeyFilename.Text;

                if (bAddToList)
                {
                    this.oConfig.Domains.Add(oCurrentDomain);
                    this.lbxDomains.Items.Add(oCurrentDomain);
                    this.lbxDomains.SelectedItem = oCurrentDomain;
                }

                if (this.SaveDkimSignerConfig())
                {
                    this.btDomainSave.Enabled = false;
                    this.btDomainDelete.Enabled = true;
                }
            }
            else
            {
                MessageBox.Show("You first need to fix the errors in your domain configuration before saving.", "Config error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}