using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Server1
{

    public class User : DbContext
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Password { get; set; }

        public byte[] key { get; set; }
    }
}
