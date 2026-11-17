//! Convert the supported phone XAP's ASF/WMA v2 songs to QOA for FNA.
//! This intentionally accepts only the known packet layout; callers verify the XAP identity.
use gameframe_wma::{Decoder, Params, WMAV2};
use qoaudio::{QoaDesc, QoaEncoder, QOA_FRAME_LEN};
use std::{env, fs, io::{self, Write}};

const ASF_HEADER: [u8; 16] = [0x30, 0x26, 0xb2, 0x75, 0x8e, 0x66, 0xcf, 0x11, 0xa6, 0xd9, 0, 0xaa, 0, 0x62, 0xce, 0x6c];
const ASF_STREAM: [u8; 16] = [0x91, 0x07, 0xdc, 0xb7, 0xb7, 0xa9, 0xcf, 0x11, 0x8e, 0xe6, 0, 0xc0, 0x0c, 0x20, 0x53, 0x65];
const ASF_DATA: [u8; 16] = [0x36, 0x26, 0xb2, 0x75, 0x8e, 0x66, 0xcf, 0x11, 0xa6, 0xd9, 0, 0xaa, 0, 0x62, 0xce, 0x6c];
const PACKET_BYTES: usize = 8948;
const AUDIO_BYTES: usize = 8917;
const PACKET_PREFIX: [u8; 6] = [0x82, 0, 0, 0x08, 0x5d, 0x04];

fn invalid(message: &'static str) -> io::Error { io::Error::new(io::ErrorKind::InvalidData, message) }
fn read_u16(data: &[u8], offset: usize) -> io::Result<u16> {
    Ok(u16::from_le_bytes(data.get(offset..offset+2).ok_or_else(|| invalid("truncated ASF"))?.try_into().unwrap()))
}
fn read_u32(data: &[u8], offset: usize) -> io::Result<u32> {
    Ok(u32::from_le_bytes(data.get(offset..offset+4).ok_or_else(|| invalid("truncated ASF"))?.try_into().unwrap()))
}
fn read_u64(data: &[u8], offset: usize) -> io::Result<u64> {
    Ok(u64::from_le_bytes(data.get(offset..offset+8).ok_or_else(|| invalid("truncated ASF"))?.try_into().unwrap()))
}

fn convert(input: &[u8], output: &mut impl Write) -> Result<(), Box<dyn std::error::Error>> {
    if input.get(..16) != Some(ASF_HEADER.as_slice()) { return Err(invalid("not ASF").into()); }
    let header_len = usize::try_from(read_u64(input, 16)?)?;
    if !(30..=input.len().saturating_sub(50)).contains(&header_len) { return Err(invalid("invalid ASF header length").into()); }
    let mut stream = None;
    let mut offset = 30;
    while offset + 24 <= header_len {
        let length = usize::try_from(read_u64(input, offset+16)?)?;
        if length < 24 || length > header_len-offset { return Err(invalid("invalid ASF object length").into()); }
        if input.get(offset..offset+16) == Some(ASF_STREAM.as_slice()) {
            if stream.is_some() { return Err(invalid("multiple ASF streams").into()); }
            stream = Some(offset);
        }
        offset += length;
    }
    let stream = stream.ok_or_else(|| invalid("missing ASF stream"))?;
    let wave = stream + 24 + 54;
    let params = Params {
        tag: read_u16(input, wave)?,
        channels: read_u16(input, wave+2)?,
        sample_rate: read_u32(input, wave+4)?,
        avg_bytes_per_sec: read_u32(input, wave+8)?,
        block_align: read_u16(input, wave+12)?,
        flags: read_u16(input, wave+18+4)?,
    };
    if params.tag != WMAV2 || params.channels != 2 || params.sample_rate != 44_100 ||
        params.avg_bytes_per_sec != 24_002 || usize::from(params.block_align) != AUDIO_BYTES ||
        read_u16(input, wave+14)? != 16 || read_u16(input, wave+16)? != 10 || params.flags != 15 {
        return Err(invalid("unsupported WMA format").into());
    }
    if input.get(header_len..header_len+16) != Some(ASF_DATA.as_slice()) { return Err(invalid("missing ASF data").into()); }
    let data_len = usize::try_from(read_u64(input, header_len+16)?)?;
    let count = usize::try_from(read_u64(input, header_len+40)?)?;
    let start = header_len + 50;
    if !(1..=3000).contains(&count) || data_len != 50 + count*PACKET_BYTES ||
        input.len() != start + count*PACKET_BYTES {
        return Err(invalid("unexpected ASF packet count or size").into());
    }

    let mut decoder = Decoder::new(&params)?;
    let mut pcm = Vec::new();
    for i in 0..count {
        let packet = &input[start+i*PACKET_BYTES..start+(i+1)*PACKET_BYTES];
        if packet.get(..6) != Some(PACKET_PREFIX.as_slice()) || packet[9..13] != [0, 0x73, 1, 1] ||
            packet[14..23] != [0, 0, 0, 0, 8, 0xd5, 0x22, 0, 0] || packet[26] != 0 ||
            packet[PACKET_BYTES-4..] != [0, 0, 0, 0] {
            return Err(invalid("unsupported ASF packet layout").into());
        }
        decoder.decode_packet(&packet[27..27+AUDIO_BYTES], &mut pcm)?;
    }
    decoder.flush(&mut pcm);
    if pcm.len() < 2 || pcm.len() % 2 != 0 { return Err(invalid("empty or incomplete audio").into()); }

    let desc = QoaDesc { channels: 2, sample_rate: 44_100, samples: (pcm.len()/2).try_into()? };
    let mut encoder = QoaEncoder::new(&desc)?;
    encoder.write_header(&mut *output)?;
    let samples_per_frame = QOA_FRAME_LEN * 2;
    for frame in pcm.chunks(samples_per_frame) {
        let samples = gameframe_wma::to_i16(frame);
        encoder.encode_frame(&samples, &mut *output)?;
    }
    Ok(())
}

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let args: Vec<_> = env::args().collect();
    if args.len() != 3 {
        eprintln!("Usage: port-audio-converter <supported.wma> <new.qoa>");
        std::process::exit(2);
    }
    let input = fs::read(&args[1])?;
    let mut output = fs::File::create(&args[2])?;
    if let Err(error) = convert(&input, &mut output) {
        drop(output);
        let _ = fs::remove_file(&args[2]);
        return Err(error);
    }
    Ok(())
}
