using static System.Windows.Forms.VisualStyles.VisualStyleElement.TreeView;
using System;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace CriaProfiles  
{
    partial class SlEstilos
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SlEstilos));
            pictureBox1 = new PictureBox();
            label1 = new Label();
            CBSuperficie = new ComboBox();
            CBStyleAL = new ComboBox();
            label2 = new Label();
            CBLabels = new ComboBox();
            label3 = new Label();
            CBStylePV = new ComboBox();
            label4 = new Label();
            CBBandSets = new ComboBox();
            label5 = new Label();
            BTCancelar = new Button();
            BTok = new Button();
            BTTrp = new RadioButton();
            BTDre = new RadioButton();
            groupBox1 = new GroupBox();
            groupBox2 = new GroupBox();
            groupBox3 = new GroupBox();
            groupBox4 = new GroupBox();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox4.SuspendLayout();
            SuspendLayout();
            // 
            // pictureBox1
            // 
            pictureBox1.BackColor = Color.Transparent;
            pictureBox1.BackgroundImageLayout = ImageLayout.Center;
            pictureBox1.Enabled = false;
            pictureBox1.Image = (Image)resources.GetObject("pictureBox1.Image");
            pictureBox1.Location = new Point(8, 8);
            pictureBox1.Margin = new Padding(3, 2, 3, 2);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(75, 75);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.FlatStyle = FlatStyle.Flat;
            label1.Font = new Font("SansSerif", 9.749999F);
            label1.ForeColor = Color.Black;
            label1.ImageAlign = ContentAlignment.MiddleLeft;
            label1.Location = new Point(9, 35);
            label1.Name = "label1";
            label1.Size = new Size(131, 15);
            label1.TabIndex = 1;
            label1.Text = "Selecionar Superfície";
            label1.TextAlign = ContentAlignment.MiddleRight;
            // 
            // CBSuperficie
            // 
            CBSuperficie.BackColor = SystemColors.Info;
            CBSuperficie.DropDownStyle = ComboBoxStyle.DropDownList;
            CBSuperficie.DropDownWidth = 300;
            CBSuperficie.FlatStyle = FlatStyle.Popup;
            CBSuperficie.Font = new Font("SansSerif", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 2);
            CBSuperficie.FormattingEnabled = true;
            CBSuperficie.Location = new Point(160, 34);
            CBSuperficie.Margin = new Padding(3, 2, 3, 2);
            CBSuperficie.Name = "CBSuperficie";
            CBSuperficie.Size = new Size(244, 20);
            CBSuperficie.TabIndex = 2;
            // 
            // CBStyleAL
            // 
            CBStyleAL.BackColor = SystemColors.Info;
            CBStyleAL.DropDownStyle = ComboBoxStyle.DropDownList;
            CBStyleAL.DropDownWidth = 300;
            CBStyleAL.FlatStyle = FlatStyle.Popup;
            CBStyleAL.Font = new Font("SansSerif", 8.25F);
            CBStyleAL.FormattingEnabled = true;
            CBStyleAL.Location = new Point(159, 32);
            CBStyleAL.Margin = new Padding(3, 2, 3, 2);
            CBStyleAL.Name = "CBStyleAL";
            CBStyleAL.Size = new Size(244, 20);
            CBStyleAL.TabIndex = 4;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.FlatStyle = FlatStyle.Flat;
            label2.Font = new Font("SansSerif", 9.749999F);
            label2.ForeColor = Color.Black;
            label2.ImageAlign = ContentAlignment.MiddleRight;
            label2.Location = new Point(6, 37);
            label2.Name = "label2";
            label2.Size = new Size(113, 15);
            label2.TabIndex = 3;
            label2.Text = "Style Alinhamento";
            // 
            // CBLabels
            // 
            CBLabels.BackColor = SystemColors.Info;
            CBLabels.DropDownStyle = ComboBoxStyle.DropDownList;
            CBLabels.DropDownWidth = 300;
            CBLabels.FlatStyle = FlatStyle.Popup;
            CBLabels.Font = new Font("SansSerif", 8.25F);
            CBLabels.FormattingEnabled = true;
            CBLabels.Location = new Point(157, 39);
            CBLabels.Margin = new Padding(3, 2, 3, 2);
            CBLabels.Name = "CBLabels";
            CBLabels.Size = new Size(244, 20);
            CBLabels.TabIndex = 6;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.FlatStyle = FlatStyle.Flat;
            label3.Font = new Font("SansSerif", 9.749999F);
            label3.ForeColor = Color.Black;
            label3.ImageAlign = ContentAlignment.MiddleRight;
            label3.Location = new Point(6, 44);
            label3.Name = "label3";
            label3.Size = new Size(121, 15);
            label3.TabIndex = 5;
            label3.Text = "Labels Alinhamento";
            label3.TextAlign = ContentAlignment.MiddleRight;
            // 
            // CBStylePV
            // 
            CBStylePV.BackColor = SystemColors.Info;
            CBStylePV.Cursor = Cursors.Hand;
            CBStylePV.DropDownStyle = ComboBoxStyle.DropDownList;
            CBStylePV.DropDownWidth = 300;
            CBStylePV.FlatStyle = FlatStyle.Popup;
            CBStylePV.Font = new Font("SansSerif", 8.25F);
            CBStylePV.FormattingEnabled = true;
            CBStylePV.Location = new Point(159, 64);
            CBStylePV.Margin = new Padding(3, 2, 3, 2);
            CBStylePV.Name = "CBStylePV";
            CBStylePV.Size = new Size(244, 20);
            CBStylePV.TabIndex = 8;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.FlatStyle = FlatStyle.Flat;
            label4.Font = new Font("SansSerif", 9.749999F);
            label4.ForeColor = Color.Black;
            label4.ImageAlign = ContentAlignment.MiddleRight;
            label4.Location = new Point(6, 69);
            label4.Name = "label4";
            label4.Size = new Size(110, 15);
            label4.TabIndex = 7;
            label4.Text = "Style Profile View";
            // 
            // CBBandSets
            // 
            CBBandSets.BackColor = SystemColors.Info;
            CBBandSets.Cursor = Cursors.Hand;
            CBBandSets.DropDownStyle = ComboBoxStyle.DropDownList;
            CBBandSets.DropDownWidth = 300;
            CBBandSets.FlatStyle = FlatStyle.Popup;
            CBBandSets.Font = new Font("SansSerif", 8.25F);
            CBBandSets.FormattingEnabled = true;
            CBBandSets.Location = new Point(157, 73);
            CBBandSets.Margin = new Padding(3, 2, 3, 2);
            CBBandSets.MaxLength = 300;
            CBBandSets.Name = "CBBandSets";
            CBBandSets.Size = new Size(244, 20);
            CBBandSets.TabIndex = 10;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.FlatStyle = FlatStyle.Flat;
            label5.Font = new Font("SansSerif", 9.749999F);
            label5.ForeColor = Color.Black;
            label5.ImageAlign = ContentAlignment.MiddleRight;
            label5.Location = new Point(9, 78);
            label5.Name = "label5";
            label5.Size = new Size(68, 15);
            label5.TabIndex = 9;
            label5.Text = "Band Sets";
            // 
            // BTCancelar
            // 
            BTCancelar.BackColor = Color.WhiteSmoke;
            BTCancelar.BackgroundImageLayout = ImageLayout.None;
            BTCancelar.Cursor = Cursors.Hand;
            BTCancelar.Font = new Font("SansSerif", 11F);
            BTCancelar.ForeColor = SystemColors.ActiveCaptionText;
            BTCancelar.Location = new Point(342, 620);
            BTCancelar.Margin = new Padding(3, 2, 3, 2);
            BTCancelar.Name = "BTCancelar";
            BTCancelar.Size = new Size(90, 30);
            BTCancelar.TabIndex = 11;
            BTCancelar.Text = "Cancelar";
            BTCancelar.UseVisualStyleBackColor = false;
            BTCancelar.Click += BTSelectCancelar;
            // 
            // BTok
            // 
            BTok.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BTok.BackColor = Color.WhiteSmoke;
            BTok.Cursor = Cursors.Hand;
            BTok.Font = new Font("SansSerif", 11F);
            BTok.ForeColor = SystemColors.ActiveCaptionText;
            BTok.Location = new Point(246, 620);
            BTok.Margin = new Padding(3, 2, 3, 2);
            BTok.Name = "BTok";
            BTok.Size = new Size(90, 30);
            BTok.TabIndex = 12;
            BTok.Text = "Criar";
            BTok.UseVisualStyleBackColor = false;
            BTok.Click += BTok_Click;
            // 
            // BTTrp
            // 
            BTTrp.AutoSize = true;
            BTTrp.Checked = true;
            BTTrp.FlatStyle = FlatStyle.System;
            BTTrp.Font = new Font("SansSerif", 9.749999F);
            BTTrp.Location = new Point(9, 76);
            BTTrp.Margin = new Padding(3, 2, 3, 2);
            BTTrp.Name = "BTTrp";
            BTTrp.Size = new Size(162, 20);
            BTTrp.TabIndex = 13;
            BTTrp.TabStop = true;
            BTTrp.Text = "Projeto Terraplenagem";
            BTTrp.UseVisualStyleBackColor = true;
            BTTrp.CheckedChanged += BTTrpCheck;
            // 
            // BTDre
            // 
            BTDre.AutoSize = true;
            BTDre.FlatStyle = FlatStyle.System;
            BTDre.Font = new Font("SansSerif", 9.749999F);
            BTDre.Location = new Point(9, 53);
            BTDre.Margin = new Padding(3, 2, 3, 2);
            BTDre.Name = "BTDre";
            BTDre.Size = new Size(135, 20);
            BTDre.TabIndex = 14;
            BTDre.Text = "Projeto Drenagem";
            BTDre.UseVisualStyleBackColor = true;
            BTDre.CheckedChanged += BTDreCheck;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(CBSuperficie);
            groupBox1.Controls.Add(label1);
            groupBox1.FlatStyle = FlatStyle.System;
            groupBox1.Font = new Font("SansSerif", 11F);
            groupBox1.Location = new Point(17, 225);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(415, 88);
            groupBox1.TabIndex = 15;
            groupBox1.TabStop = false;
            groupBox1.Text = "Superfícies";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(CBStylePV);
            groupBox2.Controls.Add(label4);
            groupBox2.Controls.Add(CBStyleAL);
            groupBox2.Controls.Add(label2);
            groupBox2.Font = new Font("SansSerif", 11F);
            groupBox2.Location = new Point(17, 332);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(414, 97);
            groupBox2.TabIndex = 16;
            groupBox2.TabStop = false;
            groupBox2.Text = "Estilos";
            // 
            // groupBox3
            // 
            groupBox3.BackColor = Color.White;
            groupBox3.Controls.Add(CBBandSets);
            groupBox3.Controls.Add(label5);
            groupBox3.Controls.Add(CBLabels);
            groupBox3.Controls.Add(label3);
            groupBox3.Font = new Font("SansSerif", 11F);
            groupBox3.Location = new Point(17, 452);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(414, 108);
            groupBox3.TabIndex = 17;
            groupBox3.TabStop = false;
            groupBox3.Text = "Rótulos";
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(BTDre);
            groupBox4.Controls.Add(BTTrp);
            groupBox4.Font = new Font("SansSerif", 11F);
            groupBox4.Location = new Point(17, 98);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(414, 116);
            groupBox4.TabIndex = 18;
            groupBox4.TabStop = false;
            groupBox4.Text = "Disciplina";
            // 
            // SlEstilos
            // 
            AcceptButton = BTok;
            AccessibleRole = AccessibleRole.None;
            AutoScaleDimensions = new SizeF(6F, 12F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            BackgroundImageLayout = ImageLayout.None;
            ClientSize = new Size(448, 677);
            ControlBox = false;
            Controls.Add(pictureBox1);
            Controls.Add(groupBox4);
            Controls.Add(groupBox3);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            Controls.Add(BTok);
            Controls.Add(BTCancelar);
            Cursor = Cursors.Hand;
            Font = new Font("SansSerif", 8.25F);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Margin = new Padding(3, 2, 3, 2);
            MinimizeBox = false;
            Name = "SlEstilos";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Alinhamentos e Perfis";
            Load += SlEstilos_Load;
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private PictureBox pictureBox1;
        private Label label1;
        private ComboBox CBSuperficie;
        private ComboBox CBStyleAL;
        private Label label2;
        private ComboBox CBLabels;
        private Label label3;
        private ComboBox CBStylePV;
        private Label label4;
        private ComboBox CBBandSets;
        private Label label5;
        private Button BTCancelar;
        private Button BTok;
        private RadioButton BTTrp;
        private RadioButton BTDre;
        private GroupBox groupBox1;
        private GroupBox groupBox2;
        private GroupBox groupBox3;
        private GroupBox groupBox4;
    }
}