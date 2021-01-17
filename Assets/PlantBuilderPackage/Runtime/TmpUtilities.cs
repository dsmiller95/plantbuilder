﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace PlantBuilder
{
    public static class Extensions
    {
        public static Vector3 AbsoluteValue(this Vector3 self)
        {
            return new Vector3(
                Mathf.Abs(self.x),
                Mathf.Abs(self.y),
                Mathf.Abs(self.z));
        }
    }
}
