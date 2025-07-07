using System;
using System.Windows.Forms;

namespace FieldGraphX
{
    partial class FieldGraphXControl
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.cmbEntities = new System.Windows.Forms.ComboBox();
            this.cmbFields = new System.Windows.Forms.ComboBox();
            this.lblEntity = new System.Windows.Forms.Label();
            this.lblField = new System.Windows.Forms.Label();
            this.btnAnalyze = new System.Windows.Forms.Button();
            this.toolStripMenu = new System.Windows.Forms.ToolStrip();
            this.tsbClose = new System.Windows.Forms.ToolStripButton();
            this.tssSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.tsbToggleDarkMode = new System.Windows.Forms.ToolStripButton();
            this.flpResults = new System.Windows.Forms.FlowLayoutPanel();
            this.flowVisualizer1 = new FlowVisualizer();
            this.button1 = new System.Windows.Forms.Button();
            this.toolStripMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // cmbEntities
            // 
            this.cmbEntities.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.cmbEntities.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            this.cmbEntities.FormattingEnabled = true;
            this.cmbEntities.Location = new System.Drawing.Point(80, 27);
            this.cmbEntities.Margin = new System.Windows.Forms.Padding(2);
            this.cmbEntities.Name = "cmbEntities";
            this.cmbEntities.Size = new System.Drawing.Size(135, 21);
            this.cmbEntities.TabIndex = 0;
            this.cmbEntities.SelectedIndexChanged += new System.EventHandler(this.cmbEntities_SelectedIndexChanged);
            // 
            // cmbFields
            // 
            this.cmbFields.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.cmbFields.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            this.cmbFields.FormattingEnabled = true;
            this.cmbFields.Location = new System.Drawing.Point(80, 52);
            this.cmbFields.Margin = new System.Windows.Forms.Padding(2);
            this.cmbFields.Name = "cmbFields";
            this.cmbFields.Size = new System.Drawing.Size(135, 21);
            this.cmbFields.TabIndex = 1;
            // 
            // lblEntity
            // 
            this.lblEntity.AutoSize = true;
            this.lblEntity.Location = new System.Drawing.Point(13, 30);
            this.lblEntity.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblEntity.Name = "lblEntity";
            this.lblEntity.Size = new System.Drawing.Size(36, 13);
            this.lblEntity.TabIndex = 2;
            this.lblEntity.Text = "Entity:";
            // 
            // lblField
            // 
            this.lblField.AutoSize = true;
            this.lblField.Location = new System.Drawing.Point(13, 54);
            this.lblField.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblField.Name = "lblField";
            this.lblField.Size = new System.Drawing.Size(32, 13);
            this.lblField.TabIndex = 3;
            this.lblField.Text = "Field:";
            // 
            // btnAnalyze
            // 
            this.btnAnalyze.Location = new System.Drawing.Point(0, 0);
            this.btnAnalyze.Name = "btnAnalyze";
            this.btnAnalyze.Size = new System.Drawing.Size(75, 23);
            this.btnAnalyze.TabIndex = 0;
            // 
            // toolStripMenu
            // 
            this.toolStripMenu.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.toolStripMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbClose,
            this.tssSeparator1,
            this.tsbToggleDarkMode});
            this.toolStripMenu.Location = new System.Drawing.Point(0, 0);
            this.toolStripMenu.Name = "toolStripMenu";
            this.toolStripMenu.Size = new System.Drawing.Size(1037, 25);
            this.toolStripMenu.TabIndex = 0;
            this.toolStripMenu.Text = "toolStrip1";
            // 
            // tsbClose
            // 
            this.tsbClose.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbClose.Name = "tsbClose";
            this.tsbClose.Size = new System.Drawing.Size(86, 22);
            this.tsbClose.Text = "Close this tool";
            // 
            // tssSeparator1
            // 
            this.tssSeparator1.Name = "tssSeparator1";
            this.tssSeparator1.Size = new System.Drawing.Size(6, 25);
            // 
            // tsbToggleDarkMode
            // 
            this.tsbToggleDarkMode.Name = "tsbToggleDarkMode";
            this.tsbToggleDarkMode.Size = new System.Drawing.Size(69, 22);
            this.tsbToggleDarkMode.Text = "Dark Mode";
            this.tsbToggleDarkMode.Click += new System.EventHandler(this.tsbToggleDarkMode_Click);
            // 
            // flpResults
            // 
            this.flpResults.AutoScroll = true;
            this.flpResults.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flpResults.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flpResults.Location = new System.Drawing.Point(0, 0);
            this.flpResults.Margin = new System.Windows.Forms.Padding(2);
            this.flpResults.Name = "flpResults";
            this.flpResults.Size = new System.Drawing.Size(1037, 391);
            this.flpResults.TabIndex = 6;
            this.flpResults.Visible = false;
            // 
            // flowVisualizer1
            // 
            this.flowVisualizer1.BackColor = System.Drawing.Color.White;
            this.flowVisualizer1.Location = new System.Drawing.Point(15, 75);
            this.flowVisualizer1.Margin = new System.Windows.Forms.Padding(2);
            this.flowVisualizer1.Name = "flowVisualizer1";
            this.flowVisualizer1.Padding = new System.Windows.Forms.Padding(5);
            this.flowVisualizer1.Size = new System.Drawing.Size(996, 305);
            this.flowVisualizer1.TabIndex = 9;
            this.flowVisualizer1.Text = "flowVisualizer1";
            this.flowVisualizer1.TriggerFlows = null;
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(235, 24);
            this.button1.Margin = new System.Windows.Forms.Padding(2);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(103, 50);
            this.button1.TabIndex = 7;
            this.button1.Text = "Analyze herachy";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // FieldGraphXControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.toolStripMenu);
            this.Controls.Add(this.flowVisualizer1);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.cmbEntities);
            this.Controls.Add(this.cmbFields);
            this.Controls.Add(this.lblEntity);
            this.Controls.Add(this.lblField);
            this.Controls.Add(this.flpResults);
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "FieldGraphXControl";
            this.Size = new System.Drawing.Size(1037, 391);
            this.OnCloseTool += new System.EventHandler(this.FieldGraphXControl_OnCloseTool);
            this.Load += new System.EventHandler(this.FieldGraphXControl_Load);
            this.toolStripMenu.ResumeLayout(false);
            this.toolStripMenu.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }


        private System.Windows.Forms.ToolStrip toolStripMenu;
        private System.Windows.Forms.ToolStripButton tsbClose;
        private System.Windows.Forms.ToolStripButton tsbToggleDarkMode;
        private System.Windows.Forms.ToolStripSeparator tssSeparator1;

        private System.Windows.Forms.Label lblEntity;
        private System.Windows.Forms.Label lblField;
        private System.Windows.Forms.Button btnAnalyze;
        private ComboBox cmbEntities;
        private ComboBox cmbFields;
        private FlowLayoutPanel flpResults;
        private Button button1;
        private FlowVisualizer flowVisualizer1;
    }
}
