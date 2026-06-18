using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Autodesk.Civil.DatabaseServices;
using System.Security.Cryptography;




namespace CriaProfiles
{
    public partial class SlEstilos : Form
    {

        Document Cad = Manager.DocCad;
        CivilDocument doc = Manager.DocCivil;
        Editor ed = Manager.DocEditor;
        Database db = Manager.DocData;

        //Construtor
        public SlEstilos()
        {
            InitializeComponent();
        }

        //Propriedade para verificar se o usuario clicou em OK
        public bool OK { get; set; }

        public bool Trp { get; set; }






        //Cria lista de superficies selecionadas
        public List<string> Superficies = new List<string>();

        //Cria lista de estilos de alinhamento
        public List<string> AlStyle = new List<string>();

        //Cria lista de labels de alinhamento
        public List<string> AlLabel = new List<string>();

        //Cria lista de estilos de Profile View
        public List<ObjectId> PVStyle = new List<ObjectId>();

        //Cria lista de estilos de Band Set
        public List<ObjectId> BandSet = new List<ObjectId>();

        //Metodo para pegar a superficie selecionada
        public string SuperficieSelecionada
        {
            get
            {
                return Superficies[CBSuperficie.SelectedIndex];
            }
        }

        //Metodo para pegar o estilo de alinhamento selecionado
        public string EstiloAlSelecionado
        {
            get
            {
                return AlStyle[CBStyleAL.SelectedIndex];
            }
        }

        //Metodo para carregar os estilos de labels
        public string LabelSelecionado
        {
            get
            {
                return AlLabel[CBLabels.SelectedIndex];
            }
        }


        public string NomeAlTrp(int contAL)
        {

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                string NomeAlinhamento = "AL_TRECHO-0" + contAL;

                foreach (ObjectId id in doc.GetAlignmentIds())
                {

                    Alignment al = (Alignment)tr.GetObject(id, OpenMode.ForRead);
                    while (al.Name == NomeAlinhamento)
                    {
                        contAL++;
                        NomeAlinhamento = "AL_TRECHO-0" + contAL;
                    }

                }

                tr.Commit();
                return NomeAlinhamento;
            }

        }


        //Metodo para pegar o estilo de Profile View selecionado
        public ObjectId PVSelecionado
        {
            get
            {
                return PVStyle[CBStylePV.SelectedIndex];
            }
        }

        //Metodo para pegar o estilo de Band Set selecionado
        public ObjectId BandSelecionado
        {
            get
            {
                return BandSet[CBBandSets.SelectedIndex];
            }
        }


        //Metodo para definir a superficie selecionada
        private void CBSelectSuperficie()
        {
            using (Transaction TransCad = Manager.DocData.TransactionManager.StartTransaction())
            {
                var index = 0;
                string nomeSuperficie;

                foreach (ObjectId Id in Manager.DocData.GetCivilSurfaceIds())
                {

                    Autodesk.Civil.DatabaseServices.Surface Superficie = (Autodesk.Civil.DatabaseServices.Surface)TransCad.GetObject(Id, OpenMode.ForRead);
                    CBSuperficie.Items.Add(Superficie.Name);
                    Superficies.Add(Superficie.Name);

                    if (index == 0)
                    {
                        nomeSuperficie = Superficie.Name;
                        CBSuperficie.SelectedIndex = CBSuperficie.Items.IndexOf(nomeSuperficie);
                        index++;
                    }

                }

                TransCad.Commit();


            }
            //Seleciona a primeira superficie da lista para aparecer no combobox
            /*if (CBSuperficie.Items.Count > 0)
            {
                CBSuperficie.SelectedIndex = 0;
            }*/
        }

        //Metodo para carregar os estilos de alinhamento
        private void CBSelectStyleAL()
        {

            var enumerator = Manager.DocCivil.Styles.AlignmentStyles.GetObjectEnumerator();
            while (enumerator.MoveNext())
            {
                // Cada item é um ObjectId
                ObjectId styleId = (ObjectId)enumerator.Current;

                // Abrir o estilo para leitura
                using (Transaction TransCad = Manager.DocData.TransactionManager.StartTransaction())
                {
                    var style = TransCad.GetObject(styleId, OpenMode.ForRead) as StyleBase;
                    if (style != null)
                    {

                        CBStyleAL.Items.Add(style.Name);
                        AlStyle.Add(style.Name);
                    }

                    if (style.Name == "PETRO - EIXO COMPLETO COM COTA")
                    {
                        CBStyleAL.SelectedIndex = CBStyleAL.Items.IndexOf(style.Name);
                    }




                    TransCad.Commit();
                }
            }
            //Seleciona a primeira superficie da lista para aparecer no combobox
            /*if (CBStyleAL.Items.Count > 0)
            {
                CBStyleAL.SelectedIndex = 0;
            }*/

        }





        //Metodo para carregar os labels de alinhamento
        private void CBSelectLabelAL()
        {

            var enumerator = Manager.DocCivil.Styles.LabelSetStyles.AlignmentLabelSetStyles.GetObjectEnumerator();
            while (enumerator.MoveNext())
            {
                // Cada item é um ObjectId
                ObjectId styleId = (ObjectId)enumerator.Current;

                // Abrir o estilo para leitura
                using (Transaction TransCad = Manager.DocData.TransactionManager.StartTransaction())
                {
                    var style = TransCad.GetObject(styleId, OpenMode.ForRead) as StyleBase;
                    if (style != null)
                    {

                        CBLabels.Items.Add(style.Name);
                        AlLabel.Add(style.Name);


                        if (style.Name == "PETRO - ESTACAS COM COTAS DE PROJETO 20M")
                        {
                            CBLabels.SelectedIndex = CBLabels.Items.IndexOf(style.Name);
                        }

                    }




                    TransCad.Commit();
                }
            }
            //Seleciona a primeira superficie da lista para aparecer no combobox


            /*if (CBLabels.Items.Count > 0)
            {
                CBLabels.SelectedIndex = 0;
            }*/

        }






        //Metodo para carregar os estilos de Profile View
        private void CBSelectedPV()
        {
            var enumerator = Manager.DocCivil.Styles.ProfileViewStyles.GetEnumerator();
            int index = 0;
            while (enumerator.MoveNext())
            {
                // Cada item é um ObjectId
                ObjectId PVstyleId = (ObjectId)enumerator.Current;

                // Abrir o estilo para leitura
                using (Transaction TransCad = Manager.DocData.TransactionManager.StartTransaction())
                {

                    var style = TransCad.GetObject(PVstyleId, OpenMode.ForRead) as StyleBase;
                    if (style != null)
                    {
                        CBStylePV.Items.Add(style.Name);
                        PVStyle.Add(PVstyleId);
                    }
                    if (index == 0)
                    {
                        CBStylePV.SelectedIndex = CBStylePV.Items.IndexOf(style.Name);
                        index++;
                    }

                    TransCad.Commit();
                }
            }

            //Seleciona a primeira superficie da lista para aparecer no combobox
            /*if (CBStylePV.Items.Count > 0)
            {
                CBStylePV.SelectedIndex = 0;
            }*/
        }



        //Metodo para carregar os estilos de Band Set
        private void CBSelectedBands()
        {
            var enumerator = Manager.DocCivil.Styles.ProfileViewBandSetStyles.GetEnumerator();
            while (enumerator.MoveNext())
            {
                // Cada item é um ObjectId
                ObjectId BSstyleId = (ObjectId)enumerator.Current;

                // Abrir o estilo para leitura
                using (Transaction TransCad = Manager.DocData.TransactionManager.StartTransaction())
                {
                    var style = TransCad.GetObject(BSstyleId, OpenMode.ForRead) as StyleBase;
                    if (style != null)
                    {
                        CBBandSets.Items.Add(style.Name);
                        BandSet.Add(BSstyleId);
                    }

                    if (style.Name == "PETRO - PERFIL TERRAPLENAGEM SIMPLES")
                    {
                        CBBandSets.SelectedIndex = CBBandSets.Items.IndexOf(style.Name);
                    }


                    TransCad.Commit();
                }
            }

            //Seleciona a primeira superficie da lista para aparecer no combobox
            /*if (CBBandSets.Items.Count > 0)
            {
                CBBandSets.SelectedIndex = 0;
            }*/

        }










        //Metodo para cancelar a seleção
        private void BTSelectCancelar(object sender, EventArgs e)
        {
            OK = false;
            this.Close();
        }

        //Metodo para confirmar a seleção
        private void BTok_Click(object sender, EventArgs e)
        {
            OK = true;
            this.Close();
        }




        private void SlEstilos_Load(object sender, EventArgs e)
        {
            CBSelectSuperficie();
            CBSelectLabelAL();
            CBSelectedBands();
            CBSelectedPV();
            CBSelectStyleAL();
            BTTrpCheck(sender, e);

        }

        private void BTTrpCheck(object sender, EventArgs e)
        {
            Trp = true;
        }

        private void BTDreCheck(object sender, EventArgs e)
        {
            Trp = false;

        }

        private void toolStripMenuItem1_Click(object sender, EventArgs e)
        {

        }

        private void CBBandSets_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

     
    }
}