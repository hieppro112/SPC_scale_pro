using ScaleScanPro.Core.Logic;
using ScaleScanPro.Core.Models;

namespace ScaleScanPro.Tests;

public class PoParserTests
{
    [Theory]
    [InlineData("A,520006656409,XYZ", "520006656409")]
    [InlineData("A, 520006656409 ,XYZ,extra", "520006656409")]
    [InlineData("520006656409", "520006656409")]
    [InlineData("  520006656409  ", "520006656409")]
    public void Valid_codes_are_parsed(string code, string expected)
    {
        Assert.True(PoParser.TryParse(code, 11, out var po));
        Assert.Equal(expected, po);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("A,,XYZ")]
    [InlineData("A,B")]
    public void Invalid_codes_are_rejected(string? code)
    {
        Assert.False(PoParser.TryParse(code, 11, out var po));
        Assert.Equal("", po);
    }
}

public class WeightCalculatorTests
{
    [Fact]
    public void Expected_is_unit_weight_rounded_times_quantity()
    {
        // 10.0 / 3 = 3.333 -> * 6 = 19.998
        Assert.Equal(19.998, WeightCalculator.ExpectedKg(10.0, 3, 6));
    }

    [Theory]
    [InlineData(0, 3, 6)]
    [InlineData(10, 0, 6)]
    [InlineData(10, 3, 0)]
    [InlineData(-1, 3, 6)]
    public void Expected_is_null_when_data_missing(double nw, int qty, double quantity)
    {
        Assert.Null(WeightCalculator.ExpectedKg(nw, qty, quantity));
    }

    [Fact]
    public void Within_absolute_tolerance()
    {
        var r = WeightCalculator.Check(10.04, 10.0, 0.05, 0);
        Assert.True(r.WithinTolerance);
        Assert.Equal(0.04, r.Difference);
    }

    [Fact]
    public void Percent_tolerance_wins_when_larger()
    {
        // 2% của 100 kg = 2 kg > 0.05
        Assert.True(WeightCalculator.Check(101.9, 100, 0.05, 2).WithinTolerance);
        Assert.False(WeightCalculator.Check(102.5, 100, 0.05, 2).WithinTolerance);
    }

    [Fact]
    public void Outside_tolerance_is_flagged()
    {
        var r = WeightCalculator.Check(12.0, 10.0, 0.05, 1);
        Assert.False(r.WithinTolerance);
        Assert.Equal(2.0, r.Difference);
        Assert.Equal(0.1, r.Allowed);
    }
}

public class StabilityDetectorTests
{
    [Fact]
    public void Needs_required_count_of_close_readings()
    {
        var d = new StabilityDetector(3, 0.01, 0.01);
        Assert.False(d.Add(5.000, out _));
        Assert.False(d.Add(5.002, out _));
        Assert.True(d.Add(5.004, out var v));
        Assert.Equal(5.002, v);
    }

    [Fact]
    public void Fluctuating_readings_are_not_stable()
    {
        var d = new StabilityDetector(3, 0.01, 0.01);
        d.Add(5.0, out _);
        d.Add(5.5, out _);
        Assert.False(d.Add(5.0, out _));
    }

    [Fact]
    public void Settling_weight_becomes_stable_after_window_slides()
    {
        var d = new StabilityDetector(3, 0.01, 0.01);
        foreach (var v in new[] { 1.0, 3.0, 5.0 })
            Assert.False(d.Add(v, out _));
        Assert.False(d.Add(10.0, out _));
        Assert.False(d.Add(10.001, out _));
        Assert.True(d.Add(10.002, out var stable));
        Assert.Equal(10.001, stable);
    }

    [Fact]
    public void Below_min_weight_is_never_stable()
    {
        var d = new StabilityDetector(2, 0.01, 0.5);
        d.Add(0.0, out _);
        Assert.False(d.Add(0.0, out _));
    }

    [Fact]
    public void Reset_clears_window()
    {
        var d = new StabilityDetector(2, 0.01, 0.01);
        d.Add(5, out _);
        d.Reset();
        Assert.False(d.Add(5, out _));
    }
}

public class NmsTests
{
    private static Detection Det(int x, int y, int w, int h, float c) => new(new BoundingBox(x, y, w, h), c, 0);

    [Fact]
    public void Overlapping_boxes_keep_highest_confidence()
    {
        var result = Nms.Apply(new[]
        {
            Det(0, 0, 100, 100, 0.5f),
            Det(5, 5, 100, 100, 0.9f),
            Det(300, 300, 50, 50, 0.4f)
        });

        Assert.Equal(2, result.Count);
        Assert.Equal(0.9f, result[0].Confidence);
        Assert.Equal(0.4f, result[1].Confidence);
    }

    [Fact]
    public void Suppressed_box_does_not_suppress_others()
    {
        // B bị A loại; C chỉ chồng lên B nên vẫn phải được giữ.
        var a = Det(0, 0, 100, 100, 0.9f);
        var b = Det(60, 0, 100, 100, 0.8f);   // IoU(a,b) = 40*100/(2*10000-4000)=0.25 -> giữ cả hai
        var c = Det(120, 0, 100, 100, 0.7f);
        var result = Nms.Apply(new[] { a, b, c }, 0.2f);
        Assert.Equal(2, result.Count); // a giữ, b bị loại (0.25>0.2), c giữ (không chồng a)
        Assert.Contains(result, d => d.Confidence == 0.9f);
        Assert.Contains(result, d => d.Confidence == 0.7f);
    }

    [Fact]
    public void Iou_of_identical_boxes_is_one_and_disjoint_is_zero()
    {
        var box = new BoundingBox(0, 0, 10, 10);
        Assert.Equal(1f, Nms.IoU(box, box));
        Assert.Equal(0f, Nms.IoU(box, new BoundingBox(20, 20, 10, 10)));
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Empty(Nms.Apply(Array.Empty<Detection>()));
    }
}

public class PageInfoTests
{
    [Fact]
    public void Computes_ranges()
    {
        var p = PageInfo.Create(2, 10, 25);
        Assert.Equal(3, p.TotalPages);
        Assert.Equal(11, p.FromIndex);
        Assert.Equal(20, p.ToIndex);
        Assert.True(p.HasPrevious);
        Assert.True(p.HasNext);
    }

    [Fact]
    public void Last_page_is_partial()
    {
        var p = PageInfo.Create(3, 10, 25);
        Assert.Equal(21, p.FromIndex);
        Assert.Equal(25, p.ToIndex);
        Assert.False(p.HasNext);
    }

    [Fact]
    public void Page_is_clamped_and_empty_is_safe()
    {
        Assert.Equal(1, PageInfo.Create(0, 10, 25).Page);
        Assert.Equal(3, PageInfo.Create(99, 10, 25).Page);

        var empty = PageInfo.Create(1, 10, 0);
        Assert.Equal(0, empty.FromIndex);
        Assert.Equal(0, empty.ToIndex);
        Assert.False(empty.HasNext);
        Assert.False(empty.HasPrevious);
    }
}

public class ScaleLineParserTests
{
    [Theory]
    [InlineData("ST,GS,  12.345 kg", 12.345)]
    [InlineData("  7 ", 7)]
    [InlineData("N 0,250", 0.25)]
    [InlineData("+003.20", 3.2)]
    public void Parses_first_number(string line, double expected)
    {
        Assert.True(ScaleLineParser.TryParse(line, out var kg));
        Assert.Equal(expected, kg, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ERR")]
    [InlineData(null)]
    public void Rejects_lines_without_numbers(string? line)
    {
        Assert.False(ScaleLineParser.TryParse(line, out _));
    }
}
