using System.Windows;
using System.Windows.Media;

namespace ClipDesk.Views;

// Resolution-independent artwork shared by the card and its category tiles.
public sealed class ContentIcon : FrameworkElement
{
    public string Kind { get; set; } = "document";
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth / 64, ActualHeight / 64));
        var purple = Kind is "folder" or "links" or "videos" or "audio" or "files";
        var top = Kind == "PDF" ? "#FF6077" : purple ? "#BD8AFF" : "#68C3FF";
        var bottom = Kind == "PDF" ? "#F32248" : purple ? "#7940F5" : "#2585F8";
        var fill = new LinearGradientBrush(ColorOf(top), ColorOf(bottom), 90);
        var white = new SolidColorBrush(ColorOf("#F8F5FF"));
        void Shape(string data, Brush brush) => dc.DrawGeometry(brush, null, Geometry.Parse(data));
        void Line(string data) => dc.DrawGeometry(null, new Pen(white, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Geometry.Parse(data));
        if (Kind == "folder")
        {
            Shape("M3,19 Q3,10 11,10 L23,10 Q27,10 31,17 L53,17 Q61,17 61,25 L61,48 Q61,55 54,55 L10,55 Q3,55 3,48 Z", new SolidColorBrush(ColorOf("#7944DD")));
            Shape("M3,25 Q3,20 10,20 L54,20 Q61,20 61,27 L61,51 Q61,58 54,58 L10,58 Q3,58 3,51 Z", fill);
            dc.DrawLine(new Pen(new SolidColorBrush(ColorOf("#70E5CFFF")), 1), new Point(9,22), new Point(55,22));
        }
        else if (Kind is "document" or "texts" or "files" or "PDF")
        {
            Shape("M15,4 L39,4 L53,18 L53,54 Q53,60 47,60 L15,60 Q9,60 9,54 L9,10 Q9,4 15,4 Z", fill);
            Shape("M39,4 L39,14 Q39,18 43,18 L53,18 Z", new SolidColorBrush(ColorOf("#99FFFFFF")));
            Line("M20,30 L42,30 M20,38 L42,38 M20,46 L35,46");
        }
        else
        {
            dc.DrawRoundedRectangle(fill, null, new Rect(4,7,56,52), 12,12);
            switch (Kind)
            {
                case "images":
                    dc.DrawEllipse(white,null,new Point(43,22),4,4);
                    Shape("M11,48 L24,31 L34,42 L41,35 L54,48 Z", white); break;
                case "videos": Shape("M26,21 L44,33 L26,45 Z",white); break;
                case "audio":
                    Line("M27,42 L27,23 L46,18 L46,38 M27,27 L46,22");
                    dc.DrawEllipse(white,null,new Point(21,44),7,6);
                    dc.DrawEllipse(white,null,new Point(40,40),7,6); break;
                case "links":
                    Line("M28,25 L33,20 C44,10 56,24 45,33 L39,39 M36,40 L31,45 C20,55 8,41 19,32 L25,26 M24,39 L40,25"); break;
                default: Line("M20,23 L44,23 M20,33 L44,33 M20,43 L36,43"); break;
            }
        }
        dc.Pop();
    }
    private static Color ColorOf(string value) => (Color)ColorConverter.ConvertFromString(value);
}
