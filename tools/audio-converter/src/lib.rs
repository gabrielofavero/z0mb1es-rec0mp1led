//! C ABI for sandboxed mobile hosts. Uses the same guarded decoder as the CLI.
#[allow(dead_code)]
mod converter {
    include!("main.rs");
    pub fn run(input: &[u8], output: &mut impl std::io::Write) -> Result<(), Box<dyn std::error::Error>> {
        convert(input, output)
    }
}

/// Paths are UTF-8, NUL-terminated strings supplied by the managed host.
/// Returns zero on success; no Rust unwind crosses the C boundary.
#[no_mangle]
pub unsafe extern "C" fn z0_audio_convert(input: *const std::ffi::c_char, output: *const std::ffi::c_char) -> i32 {
    if input.is_null() || output.is_null() { return 1; }
    let result = std::panic::catch_unwind(|| -> Result<(), Box<dyn std::error::Error>> {
        let input = std::ffi::CStr::from_ptr(input).to_str()?;
        let output = std::ffi::CStr::from_ptr(output).to_str()?;
        let bytes = std::fs::read(input)?;
        let mut file = std::fs::File::create(output)?;
        if let Err(error) = converter::run(&bytes, &mut file) {
            drop(file);
            let _ = std::fs::remove_file(output);
            return Err(error);
        }
        Ok(())
    });
    match result { Ok(Ok(())) => 0, _ => 1 }
}
