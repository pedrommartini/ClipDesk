using System.Globalization;
using System.Reflection;
using ClipDesk.Core;
using ClipDesk.Services;
using System.Text.Json;
using Supabase.Realtime.Models;

internal static class PresenceProtocolChecks
{
    public static void Run()
    {
        var backend=typeof(CloudSyncService).Assembly.GetType("ClipDesk.Services.SupabaseCloudSyncService")!;
        var encode=backend.GetMethod("PresencePayload",BindingFlags.Static|BindingFlags.NonPublic)!;
        var decode=backend.GetMethod("TryPresence",BindingFlags.Static|BindingFlags.NonPublic)!;
        var originalCulture=CultureInfo.CurrentCulture;
        var client=new Supabase.Realtime.Client("wss://example.invalid/realtime/v1");
        try
        {
            foreach(var culture in new[]{"pt-BR","en-US","de-DE"})
            {
                CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
                var sample=new CloudPresence(Guid.NewGuid().ToString(),"protocol-test",null,Guid.NewGuid().ToString("N"),
                    120.375,283.625,null,1600.125,910.875,.73,
                    [new PresenceDrag(Guid.NewGuid().ToString("N"),400.125,500.875)],null,
                    new PresenceDraft(Guid.NewGuid().ToString("N"),"pen",[new PresencePoint(20.125,30.625),new PresencePoint(44.5,70.25)]),
                    1234567890123456L);
                // Use the installed SDK's own serializer and object converter.
                var payload=(Dictionary<string,object>)encode.Invoke(null,[sample])!;
                var wire=JsonSerializer.Serialize(new BaseBroadcast {Event="presence",Payload=payload},client.SerializerSettings);
                var received=JsonSerializer.Deserialize<BaseBroadcast>(wire,client.SerializerSettings)!.Payload!;
                object?[] arguments=[received,null];
                var valid=(bool)decode.Invoke(null,arguments)!;
                var actual=arguments[1] as CloudPresence;
                Console.WriteLine($"PROTOCOL culture={culture} numberType={received["x"].GetType().Name} dragsType={received["drags"].GetType().Name} accepted={valid} drags={actual?.Drags?.Length ?? 0}");
                if(!valid || actual is null || actual.X!=sample.X || actual.Zoom!=sample.Zoom
                    || actual.Drags is not {Length:1} || actual.Drags[0].X!=sample.Drags![0].X
                    || actual.Draft?.Points is not {Length:2} || actual.Draft.Points[1].Y!=70.25
                    || actual.OriginTicks!=sample.OriginTicks)
                    throw new Exception($"Real SDK payload loses fractional pointer/camera/drag coordinates in {culture}");
                // Also exercise the alternate JsonElement representation and bad input.
                received=JsonSerializer.Deserialize<Dictionary<string,object>>(JsonSerializer.Serialize(payload))!;
                arguments=[received,null];
                if(!(bool)decode.Invoke(null,arguments)!)throw new Exception("JsonElement payload failed");
                received.Remove("originTicks");
                arguments=[received,null];
                if(!(bool)decode.Invoke(null,arguments)! || ((CloudPresence)arguments[1]!).OriginTicks!=0)
                    throw new Exception("Older presence payload without diagnostic ticks failed");
                payload["draft"]=new PresenceDraft(Guid.NewGuid().ToString("N"),"pen",
                    Enumerable.Range(0,65).Select(i=>new PresencePoint(i,i)).ToArray());
                arguments=[payload,null];
                if(!(bool)decode.Invoke(null,arguments)! || ((CloudPresence)arguments[1]!).Draft is not null)
                    throw new Exception("Unbounded drawing preview passed protocol validation.");
                var numberReader=backend.GetMethod("PayloadDouble",BindingFlags.Static|BindingFlags.NonPublic)!;
                foreach(var invalid in new object[]{true,double.NaN,double.PositiveInfinity,"not-a-number",
                    JsonSerializer.SerializeToElement(true),JsonSerializer.SerializeToElement("not-a-number")})
                {
                    arguments=[new Dictionary<string,object>{{"x",invalid}},"x",0d];
                    if((bool)numberReader.Invoke(null,arguments)!)throw new Exception("Invalid presence coordinate accepted");
                }
            }
        }
        finally { CultureInfo.CurrentCulture=originalCulture; }
        Console.WriteLine("PASS: real SDK presence payload retains pointer/camera/drag/draft coordinates and bounds previews in three cultures.");
    }
}
