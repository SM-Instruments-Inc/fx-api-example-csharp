using FxApiCSharp.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FxApiCSharp.DataTypes;

public class BeamformingSettings
{
    public int Average { get; set; } = 3;
    public float Range { get; set; } = 1f;
    public float Threshold { get; set; } = 40f;

    public bool UseAverage { get; set; } = true;
    public bool UseMultiSourceLegend { get; set; } = false;

    public BeamformingMode Mode { get; set; } = BeamformingMode.FullView;
}
