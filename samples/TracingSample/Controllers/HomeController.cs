using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OpenCensus.Trace;
using TracingSample.Models;

namespace TracingSample.Controllers
{
    public class HomeController : Controller
    {
        private readonly ITracer _tracer;
        public HomeController(ITracer tracer)
        {
            _tracer = tracer;

        }
        public IActionResult Index()
        {
            System.Threading.Thread.Sleep(1000);
            using (_tracer.SpanBuilder("test").StartScopedSpan(out var span))
            {
                span.PutAttribute("Value1", AttributeValue.StringAttributeValue("This is a value"));
                System.Threading.Thread.Sleep(500);
            }
            System.Threading.Thread.Sleep(200);
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
