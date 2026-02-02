using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    public enum StatusZadatka
    {
        NaCekanju,
        UToku,
        Zavrsen
    }

    public class ZadatakProjekta
    {
        public string Naziv { get; set; } = "";
        public string Zaposleni { get; set; } = "";   
        public StatusZadatka Status { get; set; } = StatusZadatka.NaCekanju;
        public DateTime Rok { get; set; }
        public int Prioritet { get; set; }            
        public string? Komentar { get; set; }        
        public string Menadzer { get; set; } = "";
    }
}
