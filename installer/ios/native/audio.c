/* Authored, bounded-format in-process converter for the pinned phone music.
 * No CLI entry point, subprocess, network, or process-global exit handler.
 * libav* and libvorbis retain their upstream licenses; see ../README.md. */
#include <errno.h>
#include <stdio.h>
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/audio_fifo.h>
#include <libavutil/error.h>

typedef struct {
    AVFormatContext *input, *output;
    AVCodecContext *decoder, *encoder;
    AVAudioFifo *fifo;
    AVPacket *packet, *encoded;
    AVFrame *decoded, *frame;
    int64_t samples;
} Conversion;

static int write_packets(Conversion *c)
{
    int result;
    while ((result = avcodec_receive_packet(c->encoder, c->encoded)) >= 0) {
        av_packet_rescale_ts(c->encoded, c->encoder->time_base, c->output->streams[0]->time_base);
        c->encoded->stream_index = 0;
        result = av_interleaved_write_frame(c->output, c->encoded);
        av_packet_unref(c->encoded);
        if (result < 0) return result;
    }
    return result == AVERROR(EAGAIN) || result == AVERROR_EOF ? 0 : result;
}

static int encode_fifo(Conversion *c, int flush)
{
    int block = c->encoder->frame_size;
    if (block <= 0) return AVERROR(EINVAL);
    int available;
    while ((available = av_audio_fifo_size(c->fifo)) >= block || (flush && available > 0)) {
        int samples = available < block ? available : block;
        av_frame_unref(c->frame);
        c->frame->nb_samples = samples;
        c->frame->format = c->encoder->sample_fmt;
        c->frame->sample_rate = c->encoder->sample_rate;
        int result = av_channel_layout_copy(&c->frame->ch_layout, &c->encoder->ch_layout);
        if (result < 0) return result;
        if ((result = av_frame_get_buffer(c->frame, 0)) < 0) return result;
        if (av_audio_fifo_read(c->fifo, (void **)c->frame->data, samples) != samples)
            return AVERROR(EIO);
        c->frame->pts = c->samples;
        c->samples += samples;
        if ((result = avcodec_send_frame(c->encoder, c->frame)) < 0) return result;
        if ((result = write_packets(c)) < 0) return result;
    }
    return 0;
}

static int decode_frames(Conversion *c)
{
    int result;
    while ((result = avcodec_receive_frame(c->decoder, c->decoded)) >= 0) {
        /* Both codecs support planar floats. Reject unexpected sources rather
         * than silently resampling or converting the supported song format. */
        if (c->decoded->format != AV_SAMPLE_FMT_FLTP || c->decoded->sample_rate != 44100 ||
            c->decoded->ch_layout.nb_channels != 2 ||
            (c->decoded->ch_layout.order != AV_CHANNEL_ORDER_UNSPEC &&
             av_channel_layout_compare(&c->decoded->ch_layout, &c->encoder->ch_layout) != 0))
            return AVERROR_INVALIDDATA;
        int samples = c->decoded->nb_samples;
        if ((result = av_audio_fifo_realloc(c->fifo, av_audio_fifo_size(c->fifo) + samples)) < 0)
            return result;
        if (av_audio_fifo_write(c->fifo, (void **)c->decoded->extended_data, samples) != samples)
            return AVERROR(EIO);
        av_frame_unref(c->decoded);
        if ((result = encode_fifo(c, 0)) < 0) return result;
    }
    return result == AVERROR(EAGAIN) || result == AVERROR_EOF ? 0 : result;
}

/* Every call owns its codec/muxer state, so ContentPreparer may run three calls concurrently. */
int z0_audio_convert(const char *input, const char *output, char *error, int error_capacity)
{
    Conversion c = {0};
    int result = 0, created = 0;
    const AVCodec *decoder = avcodec_find_decoder(AV_CODEC_ID_WMAV2);
    const AVCodec *encoder = avcodec_find_encoder_by_name("libvorbis");
    if (!decoder || !encoder) { result = AVERROR_ENCODER_NOT_FOUND; goto cleanup; }
    if ((result = avformat_open_input(&c.input, input, NULL, NULL)) < 0) goto cleanup;
    if ((result = avformat_find_stream_info(c.input, NULL)) < 0) goto cleanup;
    if (c.input->nb_streams != 1 || c.input->streams[0]->codecpar->codec_id != AV_CODEC_ID_WMAV2) {
        result = AVERROR_INVALIDDATA; goto cleanup;
    }
    c.decoder = avcodec_alloc_context3(decoder);
    c.encoder = avcodec_alloc_context3(encoder);
    c.packet = av_packet_alloc();
    c.encoded = av_packet_alloc();
    c.decoded = av_frame_alloc();
    c.frame = av_frame_alloc();
    if (!c.decoder || !c.encoder || !c.packet || !c.encoded || !c.decoded || !c.frame) {
        result = AVERROR(ENOMEM); goto cleanup;
    }
    if ((result = avcodec_parameters_to_context(c.decoder, c.input->streams[0]->codecpar)) < 0)
        goto cleanup;
    c.decoder->pkt_timebase = c.input->streams[0]->time_base;
    if (c.decoder->sample_rate != 44100 || c.decoder->ch_layout.nb_channels != 2) {
        result = AVERROR_INVALIDDATA; goto cleanup;
    }
    if ((result = avcodec_open2(c.decoder, decoder, NULL)) < 0) goto cleanup;
    if ((result = avformat_alloc_output_context2(&c.output, NULL, "ogg", output)) < 0) goto cleanup;
    if (!avformat_new_stream(c.output, NULL)) { result = AVERROR(ENOMEM); goto cleanup; }
    c.encoder->sample_rate = 44100;
    c.encoder->sample_fmt = AV_SAMPLE_FMT_FLTP;
    av_channel_layout_default(&c.encoder->ch_layout, 2);
    c.encoder->time_base = (AVRational){1, 44100};
    c.encoder->flags |= AV_CODEC_FLAG_QSCALE | AV_CODEC_FLAG_BITEXACT;
    c.encoder->global_quality = 5 * FF_QP2LAMBDA;
    if (c.output->oformat->flags & AVFMT_GLOBALHEADER) c.encoder->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;
    if ((result = avcodec_open2(c.encoder, encoder, NULL)) < 0) goto cleanup;
    c.output->streams[0]->time_base = c.encoder->time_base;
    if ((result = avcodec_parameters_from_context(c.output->streams[0]->codecpar, c.encoder)) < 0)
        goto cleanup;
    c.fifo = av_audio_fifo_alloc(AV_SAMPLE_FMT_FLTP, 2, c.encoder->frame_size);
    if (!c.fifo) { result = AVERROR(ENOMEM); goto cleanup; }
    c.output->flags |= AVFMT_FLAG_BITEXACT;
    if ((result = avio_open(&c.output->pb, output, AVIO_FLAG_WRITE)) < 0) goto cleanup;
    created = 1;
    if ((result = avformat_write_header(c.output, NULL)) < 0) goto cleanup;
    while ((result = av_read_frame(c.input, c.packet)) >= 0) {
        result = avcodec_send_packet(c.decoder, c.packet);
        av_packet_unref(c.packet);
        if (result < 0 || (result = decode_frames(&c)) < 0) goto cleanup;
    }
    if (result != AVERROR_EOF) goto cleanup;
    if ((result = avcodec_send_packet(c.decoder, NULL)) < 0) goto cleanup;
    if ((result = decode_frames(&c)) < 0) goto cleanup;
    if ((result = encode_fifo(&c, 1)) < 0) goto cleanup;
    if ((result = avcodec_send_frame(c.encoder, NULL)) < 0) goto cleanup;
    if ((result = write_packets(&c)) < 0) goto cleanup;
    if ((result = av_write_trailer(c.output)) < 0) goto cleanup;
    if (c.samples == 0) result = AVERROR_INVALIDDATA;

cleanup:
    if (c.output && c.output->pb) {
        int closed = avio_closep(&c.output->pb);
        if (result >= 0 && closed < 0) result = closed;
    }
    if (result < 0) {
        if (error && error_capacity > 0) av_strerror(result, error, (size_t)error_capacity);
        if (created) remove(output);
    }
    avformat_close_input(&c.input);
    avformat_free_context(c.output);
    avcodec_free_context(&c.decoder);
    avcodec_free_context(&c.encoder);
    av_audio_fifo_free(c.fifo);
    av_packet_free(&c.packet);
    av_packet_free(&c.encoded);
    av_frame_free(&c.decoded);
    av_frame_free(&c.frame);
    return result < 0 ? result : 0;
}
