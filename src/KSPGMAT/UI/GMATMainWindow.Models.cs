using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPGMAT.UI {
    [Serializable]
    public class UserVariableDef {
        public string name = "UserVar1";
        public double initialValue = 0.0;
    }

    [Serializable]
    public class UserArrayDef {
        public string name = "StateMatrix";
        public int rows = 3;
        public int cols = 3;
    }

    [Serializable]
    public class UserStringDef {
        public string name = "MissionDesc";
        public string value = "DefaultMission";
    }

    [Serializable]
    public class CustomImpulsiveBurnDef {
        public string name = "Impulsive1";
        public string origin = "Earth";
        public string axes = "VNB";
        public double element1 = 0.0; // Prograde km/s
        public double element2 = 0.0; // Normal km/s
        public double element3 = 0.0; // Radial km/s
        public bool decrementMass = false;
        public double isp = 300.0;
    }

    [Serializable]
    public class CustomSolverDef {
        public string name = "DCGTO";
        public string algorithm = "NewtonRaphson";
        public int maxIterations = 50;
        public string derivativeMethod = "ForwardDifference";
        public string reportStyle = "Normal";
    }

    [Serializable]
    public class GMATUserVariable {
        public string name = "MyVar";
        public double initialValue = 0.0;
        public string description = "";
    }

    [Serializable]
    public class GMATUserArray {
        public string name = "MyArray";
        public int rows = 3;
        public int cols = 3;
        public string description = "";
    }

    [Serializable]
    public class GMATUserString {
        public string name = "MyString";
        public string value = "Default";
        public string description = "";
    }
}
