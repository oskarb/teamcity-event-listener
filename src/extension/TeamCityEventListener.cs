﻿// ***********************************************************************
// Copyright (c) 2015 Charlie Poole
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

namespace NUnit.Engine.Listeners
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Xml;
    using Extensibility;

    // Note: Setting minimum engine version in this case is
    // purely documentary since engines prior to 3.4 do not
    // check the EngineVersion property and will try to
    // load this extension anyway.
    [Extension(Enabled = false, EngineVersion = "3.4")]
    [SuppressMessage("ReSharper", "UseNameofExpression")]
    public class TeamCityEventListener : ITestEventListener
    {
        private readonly IServiceMessageWriter _serviceMessageWriter;
        private readonly IEventConverter _eventConverter2;
        private readonly IEventConverter _eventConverter3;
        private readonly Statistics _statistics = new Statistics();

        private readonly object _lockObject = new object();
        private readonly TextWriter _outWriter;
        private readonly bool _diagnostics;
        private string _rootFlowId = string.Empty;
        private int _processId;

        // ReSharper disable once UnusedMember.Global
        public TeamCityEventListener() : this(Console.Out) { }

        public TeamCityEventListener(TextWriter outWriter)
        {
            if (outWriter == null) throw new ArgumentNullException("outWriter");
            _outWriter = outWriter;

            _serviceMessageWriter = new ServiceMessageWriter();
            var serviceMessageFactory = new ServiceMessageFactory();
            var hierarchy =  new Hierarchy();
            _eventConverter2 = new EventConverter2(serviceMessageFactory, hierarchy, _statistics);
            _eventConverter3 = new EventConverter3(serviceMessageFactory, hierarchy, _statistics);
            RootFlowId = TeamCityInfo.RootFlowId;
            _diagnostics = TeamCityInfo.Diagnostics;
            _processId = Process.GetCurrentProcess().Id;
        }

        public string RootFlowId
        {
            set
            {
                _rootFlowId = value;
                if (_rootFlowId == null)
                {
                    _rootFlowId = string.Empty;
                }
            }
        }

        public void OnTestEvent(string report)
        {
            if (_diagnostics)
            {
                _outWriter.WriteLine();
                _outWriter.WriteLine("PID_" + _processId + " !!!!{ " + report + " }!!!!");
            }

            var doc = new XmlDocument();
            doc.LoadXml(report);

            var testEvent = doc.FirstChild;
            RegisterMessage(testEvent);
        }

        public void RegisterMessage(XmlNode xmlEvent)
        {
            if (xmlEvent == null) throw new ArgumentNullException("xmlEvent");
            var messageName = xmlEvent.Name;
            if (string.IsNullOrEmpty(messageName))
            {
                return;
            }
            
            var fullName = xmlEvent.GetAttribute("fullname");
            if (string.IsNullOrEmpty(fullName))
            {
                fullName = xmlEvent.GetAttribute("testname");
                if (string.IsNullOrEmpty(fullName))
                {
                    return;
                }
            }

            var name = xmlEvent.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
            {
                name = fullName;
            }

            var id = xmlEvent.GetAttribute("id") ?? string.Empty;
            var parentId = xmlEvent.GetAttribute("parentId");

            var isNUnit3 = parentId != null;
            var eventConverter = isNUnit3 ? _eventConverter3 : _eventConverter2;

            var testEvent = new Event(_rootFlowId, messageName.ToLowerInvariant(), fullName, name, id, parentId, xmlEvent);            
            lock (_lockObject)
            {
                var sb = new StringBuilder();
                using (var writer = new StringWriter(sb))
                {
                    foreach (var messages in eventConverter.Convert(testEvent))
                    {
                        _serviceMessageWriter.Write(writer, messages);
                    }

                }

                _outWriter.Write(sb.ToString());
            }

            if (_diagnostics)
            {
                _outWriter.WriteLine("@@ NUnit3: " + isNUnit3 + ", " + _statistics + ", " + testEvent);
            }
        }
    }
}
