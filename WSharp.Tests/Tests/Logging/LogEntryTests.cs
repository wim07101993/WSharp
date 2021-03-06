﻿using FluentAssertions;
using WSharp.Logging;
using NUnit.Framework;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

namespace WSharp.Tests.Tests.Logging
{
    public class LogEntryTests
    {
        [Test]
        public void HeaderAllOptions()
        {
            var entry = new LogEntry(
                source: "source",
                tag: "tag",
                eventType: TraceEventType.Verbose,
                title: "title",
                payload: new[] { "hello", "world", "this", "is", "another", "test" },
                traceOptions: LogEntry.AllTraceOptions,
                indentLevel: 1,
                indentSize: 4);

            _ = entry.Header
                .Should()
                .Be(
                $"    [Verbose]    {entry.EventCache.DateTime.ToString(CultureInfo.InvariantCulture)}|source:tag -> title\r\n", 
                "that is the correct format");
        }

        [Test]
        public void HeaderEmpty()
        {
            var entry = new LogEntry();
            _ = entry.Header
                .Should()
                .Be($"[Verbose]    {entry.EventCache.DateTime.ToString(CultureInfo.InvariantCulture)}|HeaderEmpty\r\n");
        }

        [Test]
        public void HeaderOnlySource()
        {
            var entry = new LogEntry(source: "source");
            _ = entry.Header
                .Should()
                .Be($"[Verbose]    {entry.EventCache.DateTime.ToString(CultureInfo.InvariantCulture)}|source:HeaderOnlySource\r\n");
        }
        
        [Test]
        public void HeaderOnlyTitle()
        {
            var entry = new LogEntry(title: "title");
            _ = entry.Header
                .Should()
                .Be($"[Verbose]    {entry.EventCache.DateTime.ToString(CultureInfo.InvariantCulture)}|HeaderOnlyTitle -> title\r\n");
        }

        [Test]
        public void BodySinglePayloadWithTitle()
        {
            var entry = new LogEntry(
                title: "title", 
                indentLevel: 1, 
                indentSize: 2,
                traceOptions: LogEntry.AllTraceOptions,
                payload: new[] { "hello world"});
            _ = entry.Body
                .Should()
                .Be("      hello world\r\n");
        }

        [Test]
        public void BodySinglePayloadNoTitle()
        {
            var entry = new LogEntry(
                indentLevel: 1,
                indentSize: 2,
                payload: new ReadOnlyCollection<object>(new[] { "hello world" }));
            _ = entry.Body
                .Should()
                .Be("hello world\r\n");
        }

        [Test]
        public void BodyMultiplePayloadWithTitle()
        {
            var entry = new LogEntry(
                title: "title",
                indentSize: 2,
                payload: new[] { "hello", "world" });
            _ = entry.Body
                .Should()
                .Be(
                "    hello\r\n" +
                "    world\r\n");
        }
        
        [Test]
        public void BodyMultiplePayloadNoTitle()
        {
            var entry = new LogEntry(
                indentLevel: 1,
                indentSize: 2,
                payload: new[] { "hello", "world", "this", "is", "another", "test" });
            _ = entry.Body
                .Should()
                .Be(
                "hello\r\n" +
                "      world\r\n" +
                "      this\r\n" +
                "      is\r\n" +
                "      another\r\n" +
                "      test\r\n");
        }

        [Test]
        public void FooterAllOptions()
        {
            var entry = new LogEntry(
                source: "source",
                title: "title",
                payload: new[] { "hello", "world", "this", "is", "another", "test" },
                traceOptions: LogEntry.AllTraceOptions,
                indentLevel: 1,
                indentSize: 4);
            
            _ = entry.Footer
                .Should()
                .MatchRegex(@" {8}ProcessId=[0-9]{1,8}\|LogicalOperationStack=.*\|ThreadId=[0-9]{1,3}\|Timestamp=[0-9]*\|Callstack=.*");
        }

        [Test]
        public void FooterOnlyProcessIdAndThreadId()
        {
            var entry = new LogEntry(
                source: "source",
                title: "title",
                payload: new[] { "hello", "world", "this", "is", "another", "test" },
                traceOptions: TraceOptions.ProcessId | TraceOptions.ThreadId,
                indentLevel: 1,
                indentSize: 4);

            _ = entry.Footer
                .Should()
                .MatchRegex(@" {8}ProcessId=[0-9]{1,8}\|ThreadId=[0-9]{1,3}\|\r\n");
        }

        [Test]
        public void FooterOnlyTimeStamp()
        {
            var entry = new LogEntry(
                source: "source",
                title: "title",
                payload: new[] { "hello", "world", "this", "is", "another", "test" },
                traceOptions: TraceOptions.Timestamp,
                indentLevel: 1,
                indentSize: 1);

            _ = entry.Footer
                .Should()
                .MatchRegex(@" {2}Timestamp=[0-9]*\|\r\n");
        }

        [Test]
        public void ToStringAllOptions()
        {
            var entry = new LogEntry(
               source: "source",
               tag: "tag",
               eventType: TraceEventType.Verbose,
               title: "title",
               payload: new[] { "hello", "world", "this", "is", "another", "test" },
               traceOptions: LogEntry.AllTraceOptions,
               indentLevel: 1,
               indentSize: 4);

            _ = entry.ToString()
                .Should()
                .Be($"{entry.Header}{entry.Body}{entry.Footer}");
        }
    }
}
