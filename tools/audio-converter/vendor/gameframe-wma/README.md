# gameframe-wma

A decoder for Windows Media Audio version 2 (WMA v2, format tag `0x161`), written in Rust. WMA v2
is the codec inside xWMA files, which many Windows and Xbox games of the 2000s and 2010s use for
their sounds. It also decodes WMA v2 streams from ASF (`.wma`) files once their packets are
extracted.

- No dependencies and no `unsafe` code.
- Builds for `wasm32-unknown-unknown`, so a browser build can decode a game's sounds itself.
- Checked against FFmpeg's `wmav2` decoder: on every sound of Banished (in Un-Banished), and in
  this crate's tests on tones and noise that FFmpeg encodes. The signal-to-noise ratio must be
  at least 60 dB; the tests measure about 130 dB.

It comes from [Un-Banished](https://github.com/tanepiper/unbanished), where it decodes every
sound of the game.

## Use it

```toml
[dependencies]
gameframe-wma = { git = "https://github.com/tanepiper/game-frame" }
```

### Decode a whole xWMA stream

xWMA carries no codec extra data, so the parameters come from the `WAVEFORMATEX` header of the
file: the number of channels, the sample rate, the average bytes per second and the packet size
(`block_align`). `Params::xwma` sets the codec flags that xWMA implies.

```rust
use gameframe_wma::{decode, Params};

fn to_pcm(packets: &[u8]) -> Result<Vec<i16>, gameframe_wma::Error> {
    // 2 channels, 44,100 Hz, 6,000 bytes per second, 2,230-byte packets.
    let params = Params::xwma(2, 44_100, 6_000, 2_230);
    decode(&params, packets) // interleaved 16-bit samples
}
```

`decode` skips a packet that fails to decode, as FFmpeg does. The output ends with one extra
frame that holds the final overlap.

### Decode packet by packet

For streaming, use a `Decoder`. It writes float samples in the range -1 to 1.

```rust
use gameframe_wma::{to_i16, Decoder, Params};

fn stream(params: &Params, packets: &[u8]) -> Result<Vec<i16>, gameframe_wma::Error> {
    let mut decoder = Decoder::new(params)?;
    let mut pcm: Vec<f32> = Vec::new();
    for packet in packets.chunks(usize::from(params.block_align)) {
        decoder.decode_packet(packet, &mut pcm)?;
    }
    decoder.flush(&mut pcm); // the last overlap
    Ok(to_i16(&pcm))
}
```

### Streams from ASF files

An ASF (`.wma`) file stores the codec flags in its extra data (bytes 4 and 5). Build `Params`
with those flags. Files made by FFmpeg's encoder use `flags: 1`.

## The API

| Item | What it is |
|---|---|
| `Params` | The stream's parameters; `Params::xwma(channels, rate, bytes_per_sec, block_align)` for xWMA |
| `decode(&Params, &[u8])` | Decode a whole stream to interleaved `i16` |
| `Decoder` | `new`, `decode_packet`, `flush`, `reset`, and `channels`, `sample_rate`, `delay`, `frame_len` |
| `to_i16(&[f32])` | Convert float samples to 16-bit, with rounding and clipping |
| `Error` | `Unsupported` (a valid stream that uses a feature this decoder lacks) or `Invalid` |
| `WMAV2`, `XWMA_FLAGS` | The format tag and the flags that xWMA implies |

## Source files

| File | What it does |
|---|---|
| `src/lib.rs` | `Params`, `decode`, `to_i16`, `Error` |
| `src/decoder.rs` | Super-frames, frames and blocks |
| `src/mdct.rs` | The inverse MDCT, through a DCT-IV with a half-size complex FFT |
| `src/vlc.rs` | The table-driven decoder for prefix codes |
| `src/bits.rs` | The bit reader. Reads past the end give zero bits, as WMA frames allow |
| `src/tables.rs` | The constant tables that define the bitstream (codewords, band layouts) |

## Tests

```bash
cargo test --release -p gameframe-wma
```

`ffmpeg_parity` encodes two tones and pink noise with FFmpeg, decodes them with this crate and
with FFmpeg, and requires a signal-to-noise ratio of at least 60 dB. It skips when `ffmpeg` or
`ffprobe` is missing.

## Licence

Apache 2.0. `src/tables.rs` holds codec tables from the MIT-licensed oxideav-wma crate; see
[THIRD_PARTY.md](THIRD_PARTY.md).
