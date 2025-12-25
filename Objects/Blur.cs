using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OBSPlugin.Objects
{
    public class Blur : ICloneable
    {
        public bool Enabled = true;
        public string Name;
        public float Top;
        public float Bottom;
        public float Left;
        public float Right;
        public int Size;
        public DateTime LastEdit;
        public Blur(string name, float top, float bottom, float left, float right, int size)
        {
            Name = name;
            Top = top;
            Bottom = bottom;
            Left = left;
            Right = right;
            Size = size;
        }

        public object Clone()
        {
            return MemberwiseClone();
        }

        public override bool Equals(object? obj)
        {
            if (obj is not Blur item)
            {
                return false;
            }

            return this.Enabled == item.Enabled
                && this.Name == item.Name
                && this.Top == item.Top
                && this.Bottom == item.Bottom
                && this.Left == item.Left
                && this.Right == item.Right
                && this.Size == item.Size;
        }

        public override int GetHashCode()
        {
            return (Enabled, Name, Top, Bottom, Left, Right, Size).GetHashCode();
        }
    }
}
