﻿namespace SoundFingerprinting.Audio.NAudio
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    using global::NAudio.Wave;
    using global::NAudio.Wave.SampleProviders;

    public class NAudioService : AudioService
    {
        private static readonly IReadOnlyCollection<string> NAudioSupportedFormats = new[] { ".mp3", ".wav" };

        private readonly SamplesAggregator samplesAggregator;

        public NAudioService()
        {
            samplesAggregator = new SamplesAggregator();
        }

        ~NAudioService()
        {
            Dispose(false);
        }

        public override bool IsRecordingSupported
        {
            get
            {
                return true;
            }
        }

        public override IReadOnlyCollection<string> SupportedFormats
        {
            get
            {
                return NAudioSupportedFormats;
            }
        }

        public override float[] ReadMonoFromFile(string pathToSourceFile, int sampleRate, int seconds, int startAt)
        {
            return ReadMonoFromSource(pathToSourceFile, sampleRate, seconds, startAt, sp => new NAudioSamplesProvider(sp));
        }

        public override float[] ReadMonoFromUrlToFile(string streamUrl, string pathToFile, int sampleRate, int secondsToDownload)
        {
            float[] samples = ReadMonoFromSource(
                streamUrl,
                sampleRate,
                secondsToDownload,
                0,
                sp => new ContinuousStreamSamplesProvider(new NAudioSamplesProvider(sp)));
            WriteSamplesToFile(pathToFile, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1), samples);
            return samples;
        }

        public override float[] ReadMonoFromMicrophoneToFile(string pathToFile, int sampleRate, int secondsToRecord)
        {
            var producer = new BlockingCollection<float[]>();
            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            float[] samples;
            using (var waveIn = new WaveInEvent { WaveFormat = waveFormat })
            {
                waveIn.DataAvailable += (sender, e) =>
                    {
                        var chunk = GetFloatSamplesFromByte(e.BytesRecorded, e.Buffer);
                        producer.Add(chunk);
                    };

                waveIn.RecordingStopped += (sender, args) => producer.CompleteAdding();

                waveIn.StartRecording();

                samples = samplesAggregator.ReadSamplesFromSource(new BlockingQueueSamplesProvider(producer), secondsToRecord, sampleRate);

                waveIn.StopRecording();
            }

            WriteSamplesToFile(pathToFile, waveFormat, samples);
            return samples;
        }

        public override void RecodeFileToMonoWave(string pathToFile, string pathToRecodedFile, int sampleRate)
        {
            using (var reader = new Mp3FileReader(pathToFile))
            {
                var ieeeFloatWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
                using (var resampler = new MediaFoundationResampler(reader, ieeeFloatWaveFormat))
                {
                    WaveFileWriter.CreateWaveFile(pathToRecodedFile, resampler);
                }
            }
        }

        protected override void Dispose(bool isDisposing)
        {
        }

        private float[] ReadMonoFromSource(string pathToFile, int sampleRate, int secondsToRead, int startAtSecond, Func<SampleProviderConverterBase, ISamplesProvider> getSamplesProvider)
        {
            using (var reader = new MediaFoundationReader(pathToFile))
            {
                SeekToSecondInCaseIfRequired(startAtSecond, reader);
                var ieeeFloatWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
                using (var resampler = new MediaFoundationResampler(reader, ieeeFloatWaveFormat))
                {
                    var waveToSampleProvider = new WaveToSampleProvider(resampler);
                    return samplesAggregator.ReadSamplesFromSource(getSamplesProvider(waveToSampleProvider), secondsToRead, sampleRate);
                }
            }
        }

        private void SeekToSecondInCaseIfRequired(int startAtSecond, MediaFoundationReader reader)
        {
            if (startAtSecond > 0)
            {
                int actualSampleRate = reader.WaveFormat.SampleRate;
                int bitsPerSample = reader.WaveFormat.BitsPerSample;
                reader.Seek(actualSampleRate * bitsPerSample / 8 * startAtSecond, System.IO.SeekOrigin.Begin);
            }
        }

        private void WriteSamplesToFile(string pathToFile, WaveFormat waveFormat, float[] samples)
        {
            using (var writer = new WaveFileWriter(pathToFile, waveFormat))
            {
                writer.WriteSamples(samples, 0, samples.Length);
            }
        }

        private float[] GetFloatSamplesFromByte(int bytesRecorded, byte[] buffer)
        {
            int startIndex = 0;
            float[] chunk = new float[bytesRecorded / 4];
            int floatSampleIndex = 0;
            while (startIndex < bytesRecorded)
            {
                chunk[floatSampleIndex++] = BitConverter.ToSingle(buffer, startIndex);
                startIndex += 4;
            }

            return chunk;
        }
    }
}
