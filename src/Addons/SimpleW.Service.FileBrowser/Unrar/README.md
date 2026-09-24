# Unrar

## Implemented scope

- RAR4 container, RAR 2.9 through 4.x compression: LZ/Huffman, PPMd variant H,
  and the standard DELTA, E8, E8E9, RGB, AUDIO and ITANIUM filters.
- RAR5 container, compression version 0: LZ/Huffman and the DELTA, E8, E8E9
  and ARM filters.
- Stored method, sequential reading and preservation of solid archive state.
- Volumes named `.part01.rar/.part02.rar`, `.rar/.r00/.r01` (then `.s00`, etc.),
  and `.001/.002`. Binary `.001` fragments are concatenated at the container
  level; RAR volumes are joined at the level of each file's compressed data.
- Validation of signatures, header CRCs, volume numbering and continuity;
  CRC32 or BLAKE2sp verification of extracted data and intermediate parts when
  these fields are present (the RAR5 format makes them optional).
- Explicit rejection of encryption, links, redirections, truncated data and
  requests starting from a secondary volume.
- Quotas for entries, volumes, declared and actual output sizes, headers,
  dictionaries and filters. Cancellation during reading and decoding.

## Internal usage

`RarVolumes.Resolve(firstPath, maxVolumes, cancellation)` inventories paths.
The caller must authorize **all** these paths, check reparse points, capture
snapshots and then open the validated streams for reading. Volume discovery
must not be repeated during extraction.

`new RarArchive(streams, numericFragments, limits, cancellation)` reads headers
without decompressing files. `Entries` provides names, sizes and directory flags
to plan destinations before any writes. Names remain untrusted data: they are
never used here to open a file.

`archive.ExtractTo(index, destinationStream, cancellation)` writes an entry and
verifies its integrity. Indices must be processed in order, including directories
(pass `Stream.Null`). To skip a solid file, also decode it to `Stream.Null`.
An error or cancellation prevents further extraction on the same instance.
Source and destination streams belong to the caller and are not closed by the
decoder. The instance and its streams must not be used concurrently from
multiple threads.

The caller retains FileBrowser's responsibilities: path validation,
authorization and snapshots, collision detection, overwrite prevention
(`FileMode.CreateNew`), reparse point checks, closing streams and deleting
incomplete outputs. The new engine does not write to the filesystem using
names supplied by an archive.

## Explicit limitations

- Historical RAR 1.5/2.0/2.6 codecs are not implemented. An older RAR container
  may use them: identifying it as RAR4 therefore does not guarantee that its
  compression is supported.
- Custom RARVM programs (including the old UPCASE filter) are rejected; the
  six standard programs are identified and executed natively.
- No encryption, archive creation, SFX, reconstruction using recovery records,
  unknown unpacked sizes or RAR7/version 1 compression.
- The dictionary is limited to 256 MiB by default, configurable through
  `RarLimits` within the limits of a CLR array. PPMd memory is capped separately
  at the same value. Exceeding a limit is rejected, never ignored.
- RAR4 names without a Unicode flag use Latin-1 by default. The caller may
  supply `LegacyNameEncoding` if the original code page is known.

These limitations mean that `Unrar` does not yet provide exactly the same
features as `Unrar`.

## Format references

This implementation was written from format rules and constants, without
importing an existing library. This is not a claim of a clean-room process:
the preceding work involved SharpCompress.

- [RAR5 headers, RARLAB](https://www.rarlab.com/technote.htm)
- [Independent RAR 1.5–4.x description](https://github.com/bitplane/rar-research/blob/master/doc/RAR15_40_FORMAT_SPECIFICATION.md)
- [Independent RAR5 description](https://github.com/bitplane/rar-research/blob/master/doc/RAR5_FORMAT_SPECIFICATION.md)
- [PPMd H description](https://github.com/bitplane/rar-research/blob/master/doc/PPMD_ALGORITHM_SPECIFICATION.md)
- [BLAKE2s, RFC 7693](https://www.rfc-editor.org/rfc/rfc7693)
- [BLAKE2 and parallel mode](https://www.blake2.net/blake2.pdf)

