﻿using System;
using System.Net;
using System.Text;
using Criteo.Profiling.Tracing.Tracers.Zipkin;
using Criteo.Profiling.Tracing.Tracers.Zipkin.Thrift;
using Criteo.Profiling.Tracing.Utils;
using NUnit.Framework;
using BinaryAnnotation = Criteo.Profiling.Tracing.Tracers.Zipkin.BinaryAnnotation;
using Span = Criteo.Profiling.Tracing.Tracers.Zipkin.Span;

namespace Criteo.Profiling.Tracing.UTest.Tracers.Zipkin
{

    [TestFixture]
    class T_ThriftSpanSerializer
    {
        private const string SomeRandomAnnotation = "SomethingHappenedHere";

        private readonly Endpoint _someHost = new Endpoint { Service_name = "myService", Port = 80, Ipv4 = 123456 };

        [Test]
        public void ThriftConversionBinaryAnnotationIsCorrect()
        {
            const string key = "myKey";
            var data = Encoding.ASCII.GetBytes("hello");
            const AnnotationType type = AnnotationType.STRING;

            var binAnn = new BinaryAnnotation(key, data, type);

            var thriftBinAnn = ThriftSpanSerializer.ConvertToThrift(binAnn, _someHost);

            Assert.AreEqual(key, thriftBinAnn.Key);
            Assert.AreEqual(data, thriftBinAnn.Value);
            Assert.AreEqual(type, thriftBinAnn.Annotation_type);
            AssertEndpointIsCorrect(thriftBinAnn.Host);
        }

        [Test]
        public void ThriftConversionZipkinAnnotationIsCorrect()
        {
            var now = TimeUtils.UtcNow;
            const string value = "anything";
            var ann = new ZipkinAnnotation(now, value);

            var thriftAnn = ThriftSpanSerializer.ConvertToThrift(ann, _someHost);

            Assert.NotNull(thriftAnn);
            Assert.AreEqual(TimeUtils.ToUnixTimestamp(now), thriftAnn.Timestamp);
            Assert.AreEqual(value, thriftAnn.Value);
            AssertEndpointIsCorrect(thriftAnn.Host);
        }

        private void AssertEndpointIsCorrect(Endpoint endpoint)
        {
            Assert.AreEqual(_someHost.Service_name, endpoint.Service_name);
            Assert.AreEqual(_someHost.Port, endpoint.Port);
            Assert.AreEqual(_someHost.Ipv4, endpoint.Ipv4);
        }

        [Test]
        public void IpToIntConversionIsCorrect()
        {
            const string ipStr = "192.168.1.56";
            const int expectedIp = unchecked((int)3232235832);

            var ipAddr = IPAddress.Parse(ipStr);

            var ipInt = ThriftSpanSerializer.IpToInt(ipAddr);

            Assert.AreEqual(expectedIp, ipInt);
        }

        [TestCase(null)]
        [TestCase(123456L)]
        public void SpanCorrectlyConvertedToThrift(long? parentSpanId)
        {
            var hostIp = IPAddress.Loopback;
            const int hostPort = 1234;
            const string serviceName = "myCriteoService";
            const string methodName = "GET";

            var spanState = new SpanState(1, parentSpanId, 2, SpanFlags.None);
            var span = new Span(spanState, TimeUtils.UtcNow) { Endpoint = new IPEndPoint(hostIp, hostPort), ServiceName = serviceName, Name = methodName };

            var zipkinAnnDateTime = TimeUtils.UtcNow;
            AddClientSendReceiveAnnotations(span, zipkinAnnDateTime);
            span.AddAnnotation(new ZipkinAnnotation(zipkinAnnDateTime, SomeRandomAnnotation));

            const string binAnnKey = "http.uri";
            var binAnnVal = new byte[] { 0x00 };
            const AnnotationType binAnnType = AnnotationType.STRING;

            span.AddBinaryAnnotation(new BinaryAnnotation(binAnnKey, binAnnVal, binAnnType));

            var thriftSpan = ThriftSpanSerializer.ConvertToThrift(span);

            var expectedHost = new Endpoint()
            {
                Ipv4 = ThriftSpanSerializer.IpToInt(hostIp),
                Port = hostPort,
                Service_name = serviceName
            };

            Assert.AreEqual(1, thriftSpan.Trace_id);
            Assert.AreEqual(2, thriftSpan.Id);

            if (span.IsRoot)
            {
                Assert.IsNull(thriftSpan.Parent_id); // root span has no parent
            }
            else
            {
                Assert.AreEqual(parentSpanId, thriftSpan.Parent_id);
            }

            Assert.AreEqual(false, thriftSpan.Debug);
            Assert.AreEqual(methodName, thriftSpan.Name);

            Assert.AreEqual(3, thriftSpan.Annotations.Count);

            thriftSpan.Annotations.ForEach(ann =>
            {
                Assert.AreEqual(expectedHost, ann.Host);
                Assert.AreEqual(TimeUtils.ToUnixTimestamp(zipkinAnnDateTime), ann.Timestamp);
            });

            Assert.AreEqual(1, thriftSpan.Binary_annotations.Count);

            thriftSpan.Binary_annotations.ForEach(ann =>
            {
                Assert.AreEqual(expectedHost, ann.Host);
                Assert.AreEqual(binAnnKey, ann.Key);
                Assert.AreEqual(binAnnVal, ann.Value);
                Assert.AreEqual(binAnnType, ann.Annotation_type);
            });


            Assert.IsNull(thriftSpan.Duration);
        }

        [Test]
        [Description("Span should never be sent without required fields such as Name, ServiceName, Ipv4 or Port")]
        public void DefaultsValuesAreUsedIfNothingSpecified()
        {
            var spanState = new SpanState(1, 0, 2, SpanFlags.None);
            var span = new Span(spanState, TimeUtils.UtcNow);
            AddClientSendReceiveAnnotations(span);

            var thriftSpan = ThriftSpanSerializer.ConvertToThrift(span);
            AssertSpanHasRequiredFields(thriftSpan);

            const string defaultName = ThriftSpanSerializer.DefaultRpcMethod;
            var defaultServiceName = TraceManager.Configuration.DefaultServiceName;
            var defaultIpv4 = ThriftSpanSerializer.IpToInt(TraceManager.Configuration.DefaultEndPoint.Address);
            var defaultPort = TraceManager.Configuration.DefaultEndPoint.Port;

            Assert.AreEqual(2, thriftSpan.Annotations.Count);
            thriftSpan.Annotations.ForEach(ann =>
            {
                Assert.AreEqual(defaultServiceName, ann.Host.Service_name);
                Assert.AreEqual(defaultIpv4, ann.Host.Ipv4);
                Assert.AreEqual(defaultPort, ann.Host.Port);
            });

            Assert.AreEqual(defaultName, thriftSpan.Name);
        }

        [Test]
        public void DefaultsValuesAreNotUsedIfValuesSpecified()
        {
            var spanState = new SpanState(1, 0, 2, SpanFlags.None);
            var started = TimeUtils.UtcNow;

            // Make sure we choose something different thant the default values
            var serviceName = TraceManager.Configuration.DefaultServiceName + "_notDefault";
            var hostPort = TraceManager.Configuration.DefaultEndPoint.Port + 1;

            const string name = "myRPCmethod";

            var span = new Span(spanState, started) { Endpoint = new IPEndPoint(IPAddress.Loopback, hostPort), ServiceName = serviceName, Name = name };
            AddClientSendReceiveAnnotations(span);

            var thriftSpan = ThriftSpanSerializer.ConvertToThrift(span);
            AssertSpanHasRequiredFields(thriftSpan);

            Assert.NotNull(thriftSpan);
            Assert.AreEqual(2, thriftSpan.Annotations.Count);

            thriftSpan.Annotations.ForEach(annotation =>
            {
                Assert.AreEqual(serviceName, annotation.Host.Service_name);
                Assert.AreEqual(ThriftSpanSerializer.IpToInt(IPAddress.Loopback), annotation.Host.Ipv4);
                Assert.AreEqual(hostPort, annotation.Host.Port);
            });

            Assert.AreEqual(name, thriftSpan.Name);
        }

        [TestCase(null)]
        [TestCase(123456L)]
        public void RootSpanPropertyIsCorrect(long? parentSpanId)
        {
            var spanState = new SpanState(1, parentSpanId, 1, SpanFlags.None);
            var span = new Span(spanState, TimeUtils.UtcNow);

            Assert.AreEqual(parentSpanId == null, span.IsRoot);
        }

        [Test]
        public void WhiteSpacesAreRemovedFromServiceName()
        {
            var spanState = new SpanState(1, 0, 2, SpanFlags.None);
            var span = new Span(spanState, TimeUtils.UtcNow) { ServiceName = "my Criteo Service" };
            AddClientSendReceiveAnnotations(span);

            var thriftSpan = ThriftSpanSerializer.ConvertToThrift(span);

            Assert.AreEqual("my_Criteo_Service", thriftSpan.Annotations[0].Host.Service_name);
        }


        [TestCase(-200, false)]
        [TestCase(-1, false)]
        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(200, true)]
        public void SpanHasDurationOnlyIfValueIsPositive(int offset, bool shouldHaveDuration)
        {
            var duration = GetSpanDuration(offset, zipkinCoreConstants.CLIENT_SEND, zipkinCoreConstants.CLIENT_RECV);
            if (shouldHaveDuration)
            {
                Assert.AreEqual(offset * 1000, duration);
            }
            else
            {
                Assert.IsNull(duration);
            }
        }

        [Test]
        public void SpanDoesntHaveDurationIfIncomplete()
        {
            const int offset = 10;

            Assert.IsNull(GetSpanDuration(offset, zipkinCoreConstants.SERVER_RECV));
            Assert.IsNull(GetSpanDuration(offset, zipkinCoreConstants.SERVER_SEND));
            Assert.IsNull(GetSpanDuration(offset, zipkinCoreConstants.CLIENT_RECV));
            Assert.IsNull(GetSpanDuration(offset, zipkinCoreConstants.CLIENT_SEND));
        }

        [Test]
        public void ClientDurationIsPreferredOverServer()
        {
            var spanState = new SpanState(1, 0, 2, SpanFlags.None);
            var span = new Span(spanState, TimeUtils.UtcNow);
            const int offset = 10;

            var annotationTime = TimeUtils.UtcNow;
            span.AddAnnotation(new ZipkinAnnotation(annotationTime, zipkinCoreConstants.SERVER_RECV));
            span.AddAnnotation(new ZipkinAnnotation(annotationTime.AddMilliseconds(offset), zipkinCoreConstants.SERVER_SEND));
            span.AddAnnotation(new ZipkinAnnotation(annotationTime.AddMilliseconds(-offset), zipkinCoreConstants.CLIENT_SEND));
            span.AddAnnotation(new ZipkinAnnotation(annotationTime.AddMilliseconds(2 * offset), zipkinCoreConstants.CLIENT_RECV));

            var duration = ThriftSpanSerializer.ConvertToThrift(span).Duration;

            Assert.AreEqual(3 * offset * 1000 /* microseconds */, duration);
        }

        private static long? GetSpanDuration(int offset, string firstAnnValue, string secondAnnValue = null)
        {
            var spanState = new SpanState(1, 0, 2, SpanFlags.None);
            var span = new Span(spanState, TimeUtils.UtcNow);

            var annotationTime = TimeUtils.UtcNow;
            span.AddAnnotation(new ZipkinAnnotation(annotationTime, firstAnnValue));
            if (secondAnnValue != null) span.AddAnnotation(new ZipkinAnnotation(annotationTime.AddMilliseconds(offset), secondAnnValue));

            return ThriftSpanSerializer.ConvertToThrift(span).Duration;
        }

        private static void AddClientSendReceiveAnnotations(Span span)
        {
            AddClientSendReceiveAnnotations(span, TimeUtils.UtcNow);
        }

        private static void AddClientSendReceiveAnnotations(Span span, DateTime dateTime)
        {
            span.AddAnnotation(new ZipkinAnnotation(dateTime, zipkinCoreConstants.CLIENT_SEND));
            span.AddAnnotation(new ZipkinAnnotation(dateTime, zipkinCoreConstants.CLIENT_RECV));
        }

        private static void AssertSpanHasRequiredFields(Tracing.Tracers.Zipkin.Thrift.Span thriftSpan)
        {
            Assert.IsNotNull(thriftSpan.Id);
            Assert.IsNotNull(thriftSpan.Trace_id);
            Assert.IsNotNullOrEmpty(thriftSpan.Name);

            thriftSpan.Annotations.ForEach(annotation =>
            {
                Assert.IsNotNullOrEmpty(annotation.Host.Service_name);
                Assert.IsNotNull(annotation.Host.Ipv4);
                Assert.IsNotNull(annotation.Host.Port);

                Assert.IsNotNull(annotation.Timestamp);
                Assert.That(annotation.Timestamp, Is.GreaterThan(0));
                Assert.IsNotNullOrEmpty(annotation.Value);
            });

            if (thriftSpan.Binary_annotations != null)
            {
                thriftSpan.Binary_annotations.ForEach(annotation =>
                {
                    Assert.IsNotNullOrEmpty(annotation.Host.Service_name);
                    Assert.IsNotNull(annotation.Host.Ipv4);
                    Assert.IsNotNull(annotation.Host.Port);

                    Assert.IsNotNull(annotation.Annotation_type);
                    Assert.IsNotNull(annotation.Value);
                });
            }
        }

    }
}