using MudBlazor;

namespace WorkSphere.Services;

public static class ChartExtensions
{
    public static List<ChartSeries<double>> AsChartDataSet(this double[] data, string name = "Data")
    {
        return new List<ChartSeries<double>>
        {
            new ChartSeries<double> { Name = name, Data = data }
        };
    }
}
