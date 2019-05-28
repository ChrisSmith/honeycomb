using System;
using System.Collections.Generic;
using System.Linq;
using Honeycomb.Models;
using Microsoft.Extensions.Options;
using OpenCensus.Trace;
using OpenCensus.Trace.Export;

namespace Honeycomb.Tracing
{
    public class HoneycombExportHandler : IHandler
    {
        private readonly IHoneycombService _service;
        private readonly string _serviceName;
        private readonly IOptions<HoneycombApiSettings> _settings;
        public HoneycombExportHandler(IHoneycombService service, string serviceName, IOptions<HoneycombApiSettings> settings)
        {
            _settings = settings;
            _serviceName = serviceName;
            _service = service;
        }

        public void Export(IEnumerable<ISpanData> spanDataList)
        {
            var ev = new HoneycombEvent();

            foreach (var item in spanDataList)
            {
                _service.QueueEvent(GenerateEvent(item));
            }
        }

        private HoneycombEvent GenerateEvent(ISpanData data)
        {
            var ev = new HoneycombEvent {
                DataSetName = _settings.Value.DefaultDataSet,
                EventTime = GetStartTime(data),
                Data = data.Attributes.AttributeMap.ToDictionary(a => a.Key, a => (object)AttributeValueToString(a.Value))
            };
            ev.Data.Add("service_name", _serviceName);
            ev.Data.Add("trace.trace_id", data.Context.TraceId.ToLowerBase16());
            ev.Data.Add("trace.span_id", data.Context.SpanId.ToLowerBase16());
            ev.Data.Add("duration_ms", GetDuration(data));
            if (data.ParentSpanId != null && data.ParentSpanId.IsValid)
                ev.Data.Add("trace.parent_id", data.ParentSpanId.ToLowerBase16());

            return ev;
        }

        private long GetDuration(ISpanData data)
        {
            return data.EndTimestamp.SubtractTimestamp(data.StartTimestamp).Nanos;
        }

        private DateTime GetStartTime(ISpanData data)
        {
            return DateTimeOffset.FromUnixTimeSeconds(data.StartTimestamp.Seconds).DateTime;
        }

        private string AttributeValueToString(IAttributeValue attributeValue)
        {
            return attributeValue.Match(
                (arg) => { return arg; },
                (arg) => { return arg.ToString(); },
                (arg) => { return arg.ToString(); },
                (arg) => { return arg.ToString(); },
                (arg) => { return null; });
        }
    }
}
