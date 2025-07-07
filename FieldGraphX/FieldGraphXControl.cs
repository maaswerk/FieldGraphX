using FieldGraphX.Models;
using FlowGraphX;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using XrmToolBox.Extensibility;
using Label = System.Windows.Forms.Label;

namespace FieldGraphX
{
    public partial class FieldGraphXControl : PluginControlBase
    {
        private Settings mySettings;
        private InfoLoader myInfoLoader;
        private FlowVisualizationPanel visualizationPanel;
        private bool isDarkMode = false;


        // Dictionary zum Speichern der Flow-Positionen für das Zeichnen der Verbindungen
        private Dictionary<Guid, Rectangle> flowCardPositions = new Dictionary<Guid, Rectangle>();
        private List<FlowUsage> currentFlows = new List<FlowUsage>();
        private List<FlowUsage> allRelevantFlows = new List<FlowUsage>();

        // NEUE STRUKTUR: Gruppierung nach Feldern
        private Dictionary<string, List<FlowUsage>> fieldTriggerGroups = new Dictionary<string, List<FlowUsage>>();
        private Dictionary<string, List<FlowUsage>> fieldSetterGroups = new Dictionary<string, List<FlowUsage>>();

        public FieldGraphXControl()
        {
            InitializeComponent();
            InitializeVisualization();
        }

        private void InitializeVisualization()
        {
            visualizationPanel = new FlowVisualizationPanel();
            visualizationPanel.Dock = DockStyle.Fill;
            this.Controls.Add(visualizationPanel); // oder zu deinem Container
        }

        private void FieldGraphXControl_Load(object sender, EventArgs e)
        {
            //ShowInfoNotification("This is a notification that can lead to XrmToolBox repository", new Uri("https://github.com/MscrmTools/XrmToolBox"));

            // Loads or creates the settings for the plugin
            if (!SettingsManager.Instance.TryLoad(GetType(), out mySettings))
            {
                mySettings = new Settings();
                mySettings.LastUsedOrganizationWebappUrl = ConnectionDetail.WebApplicationUrl;
                LogWarning("Settings not found => a new settings file has been created!");
            }
            else
            {
                LogInfo("Settings found and loaded");
            }

            myInfoLoader = new InfoLoader(Service);
            cmbEntities.DataSource = myInfoLoader.LoadEntities();
            if (cmbEntities.Items.Count > 0)
            {
                cmbEntities.SelectedIndex = 0;
            }
            if (cmbEntities.Text?.Trim()?.ToLower() != "")
            {
                cmbFields.DataSource = myInfoLoader.LoadFields(cmbEntities.Text.Trim().ToLower());

            }
            else
            {
                cmbFields.DataSource = new List<string>();
            }
            cmbEntities.Text = "msdyn_workorder";
            cmbFields.Text = "ith_level_txt";
        }


        /// <summary>
        /// This event occurs when the plugin is closed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FieldGraphXControl_OnCloseTool(object sender, EventArgs e)
        {
            // Before leaving, save the settings
            SettingsManager.Instance.Save(GetType(), mySettings);
        }

        private void tsbToggleDarkMode_Click(object sender, EventArgs e)
        {
            isDarkMode = !isDarkMode;

            // Hintergrundfarbe ändern
            this.BackColor = isDarkMode ? Color.Black : Color.White;
            this.flpResults.BackColor = isDarkMode ? Color.Black : Color.White;

            // ToolStrip und andere Steuerelemente anpassen
            toolStripMenu.BackColor = isDarkMode ? Color.DarkGray : Color.LightGray;
            foreach (ToolStripItem item in toolStripMenu.Items)
            {
                item.ForeColor = isDarkMode ? Color.White : Color.Black;
            }

            // Labels und Buttons anpassen
            foreach (Control control in this.Controls)
            {
                if (control is Label label)
                {
                    label.ForeColor = isDarkMode ? Color.White : Color.Black;
                }
                else if (control is Button button)
                {
                    button.BackColor = isDarkMode ? Color.Gray : Color.LightGray;
                    button.ForeColor = isDarkMode ? Color.White : Color.Black;
                }
                else if (control is ComboBox comboBox)
                {
                    comboBox.BackColor = isDarkMode ? Color.Gray : Color.White;
                    comboBox.ForeColor = isDarkMode ? Color.White : Color.Black;
                }
            }
        }

        /// <summary>
        /// This event occurs when the connection has been updated in XrmToolBox
        /// </summary>
        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);

            if (mySettings != null && detail != null)
            {
                mySettings.LastUsedOrganizationWebappUrl = detail.WebApplicationUrl;
                LogInfo("Connection has changed to: {0}", detail.WebApplicationUrl);
            }
        }


        private void cmbEntities_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Setze die DataSource auf null, um die Items-Sammlung zurückzusetzen
            cmbFields.DataSource = null;

            // Lade die Felder basierend auf der ausgewählten Entität
            var fields = myInfoLoader.LoadFields(cmbEntities.Text.Trim().ToLower());

            // Weise die neue Datenquelle zu
            cmbFields.DataSource = fields;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            flpResults.Controls.Clear(); // FlowLayoutPanel für Ergebnisse leeren
            flpResults.AutoScroll = true;

            string entity = cmbEntities.Text.Trim().ToLower();
            string field = cmbFields.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(entity) || string.IsNullOrEmpty(field))
            {
                MessageBox.Show("Please specify entity and field.");
                return;
            }

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Flows are analyzed...",
                Work = (w, ev) =>
                {
                    var analyzer = new FlowAnalyzer(Service);
                    var hierarchy = analyzer.AnalyzeFlowsHierarchically(entity, field, "EnvID"); // Hierarchische Analyse
                    ev.Result = hierarchy;
                },
                PostWorkCallBack = ev =>
                {
                    var hierarchy = ev.Result as List<FlowUsage>;
                    //MessageBox.Show($"Anzahl der Flows: {hierarchy.Count + hierarchy.Sum(f => f.Parents.Count)}");
                    if (hierarchy != null && hierarchy.Count > 0)
                    {
                        VisualizeFlowHierarchyWithArrows(hierarchy);
                    }
                    else
                    {
                        MessageBox.Show("No flows found.");
                    }
                }
            });
        }
        public void VisualizeFlowHierarchyWithArrows(List<FlowUsage> hierarchy)
        {
            FlowVisualizer visualizer = new FlowVisualizer();
            flowVisualizer1.Size = new Size(1494, 469);
            flowVisualizer1.TriggerFlows = hierarchy;
            flowVisualizer1.Invalidate(); // Stellt sicher, dass die Darstellung aktualisiert wird
        }


        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            // Beispiel: FlowLayoutPanel anpassen
            flpResults.Width = this.ClientSize.Width - 20;
            flpResults.Height = this.ClientSize.Height - toolStripMenu.Height - 20;

            // Andere Steuerelemente dynamisch anpassen
            foreach (Control control in this.Controls)
            {
                if (control is Panel panel)
                {
                    panel.Width = this.ClientSize.Width / 2; // Beispiel: Panel halb so breit wie das Fenster
                    panel.Height = this.ClientSize.Height / 3; // Beispiel: Panel ein Drittel der Fensterhöhe
                }
            }
        }
    }
}