# Socket artwork

The current source is the user-supplied Textool.tiff, copied unchanged into
Src/Resources/Textool.tiff. Import-Textool.py extracts its two 200x512 panels
at (10,38) and (230,38), excluding the headings. The revised TIFF supplied on
22 September is used. Hardware silhouettes are cut out and moved five source
pixels down; the inserted-chip drawing follows this offset. Hardware textures,
lettering and contact pitch are retained without rescaling. The rectangular TIFF
background is discarded. The original front-panel seams are continued behind
the hardware using adjacent background samples and texture, with blended borders.

The open clamp variant moves a cutout of the supplied handle to the pivot and
repairs its former background. This remains a simplified view of the raised
handle. File selection, clamping and chip simulation behavior are unchanged.

Rectify-PkwSocket.py forwards to the new importer so the old reconstruction
cannot accidentally overwrite these assets. Previous source photographs remain
available but are not used by this pipeline.

Build: tmp/socket-photo-rebuild/8085.exe
Rendered preview: tmp/socket-photo-rebuild/socket-contact-comparison.png

Zoom alignment: PhotoSocket now uses the front panel scale and fractional image origin instead of the rounded child-window size. Regression image comparisons pass at panel widths 777, 997, 1199, 1536 and 1843 pixels. The latest user TIFF has been imported.

Lever correction: preserve the complete TIFF bent shaft and grip as one cutout. The upright grip is centered at panel-local (138,365), under the side step, rather than at the bottom screw. The socket silhouette now retains the full right side down to that step. Only the upright grip removes the donor housing fringe before foreshortening.
