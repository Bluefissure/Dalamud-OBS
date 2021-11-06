using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OBSPlugin.Objects
{
    public class Blur
    {
        public bool Enabled = true;
        public string Name;
        public float Top;
        public float Bottom;
        public float Left;
        public float Right;
        public int Size;
        public Blur(string name, float top, float bottom, float left, float right, int size)
        {
            Name = name;
            Top = top;
            Bottom = bottom;
            Left = left;
            Right = right;
            Size = size;
        }
    }
}
