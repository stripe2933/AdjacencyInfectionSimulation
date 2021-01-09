using System;
using System.Collections.Generic;
using System.Text;

namespace AIS
{
    public class Person
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Vx { get; set; }
        public double Vy { get; set; }
        public bool Infected { get; set; }

        public Person(double x, double y, bool infected = false)
        {
            X = x;
            Y = y;
            Infected = infected;
        }
    }
}
