# Sports selection artwork

These three original raster illustrations were produced with the built-in `image_gen` tool for the operator's sports-selection cards. They are stylised static background artwork, not video or UI screenshots. No generated text, club identity or Apple artwork is included. The PNG files are embedded WPF resources, decoded once at a bounded width and shared across visits. They require no network or API key when running the app.

| File | Selected generated output |
|---|---|
| `afl-animated-v1.png` | `exec-ff44171b-a664-4aa7-bf92-afd7f61b10ee.png` |
| `cricket-animated-v1.png` | `exec-3cc277fd-aa5b-4e8a-8649-61f5d7151182.png` |
| `training-animated-v1.png` | `exec-c0c83d27-3d24-4e40-b131-f7be85ef991c.png` |

The selected generated files were copied unchanged into this directory. Earlier, more photographic variants were rejected. The application adds its own runtime clipping and edge blend; those are presentation effects and do not alter the source images.

## Prompt set

The initial brief was a wide 3:1 panoramic sports-card background: dark navy left third for existing white labels, meaningful sporting subjects on the right, sophisticated animated-film/editorial illustration between painting and stylised 3D, no childish exaggeration, no flat vector clipart, no people, text, logos, interface or borders. AFL uses a red oval stitched football, four goal posts with the two central posts taller, oval turf and twilight stadium lights. Cricket uses a cream bat, three stumps with bails, red seamed ball and oval turf. Training uses three orange cones, an agility ladder, curved running lane and teal turf.

The final built-in edit prompts, applied to the corresponding initial generated images, were:

### AFL

> Redraw this exact wide sports background as a polished STYLISED ANIMATION FILM painting. The current result is much too photographic. Preserve wide 3:1 composition, dark left third, Australian rules red oval football on right, distant four goal posts with inner pair taller, stadium and twilight. Drastically simplify the shapes into elegant rounded sculpted 2.5D forms and broad painterly lighting. NO individual grass blades, NO leather pores, NO realistic clouds, NO detailed crowd, NO photography. Soft illustrated turf bands, smooth red football with a few clean seam details, chunky simplified stadium geometry, graphic painterly clouds, soft contact shadows. Think sophisticated animated sports title artwork for an adult app: appealing, rich, expressive, neither a children's cartoon nor a vector icon. Dark navy left third remains nearly empty for UI labels. No words, no logos, no UI.

### Cricket

> Make this cricket illustration significantly less realistic, closer to a polished stylised animation-film background painting with broad clean brush shapes and appealing sculpted sporting objects. Keep exact wide dark-left/right-subject composition, wooden bat, three stumps and red seamed ball. Replace individual grass blades and photo textures with broad smooth painted turf bands; simplify wood grain, ball stitching and stadium into clean expressive shapes. Paint the sky in graphic cloud masses. Elegant animated title artwork for adults, not children's cartoon, no words or logos. Keep dark left third empty for app labels.

### Training

> Make this illustration significantly less realistic, closer to a polished stylised animation-film background painting with broad clean brush shapes and appealing sculpted cones. Keep exact wide dark-left/right-subject composition and orange cones, teal turf, agility ladder, dusk stadium. Replace individual grass blades and photo textures with broad smooth painted turf bands, reduce crowd and architecture detail to graphic shapes, paint the sky in simplified expressive cloud masses. Elegant animated title artwork for adults, not children's cartoon, no text or logos. Keep dark left third empty for the app labels.


## Cartoon revision, 27 October 2026

The active resources are now `afl-cartoon-v2.png`, `cricket-cartoon-v2.png`, and `training-cartoon-v2.png`. Each is an unchanged original built-in image_gen output (2172 by 724). Earlier v1 files are retained as design history and excluded from embedded build resources.

Selected outputs: AFL `exec-433b1afa-5d40-4fe2-ac74-1c0bc34bdb36.png`; cricket `exec-cfc84729-be31-4b3e-94b7-fc70ee6c4485.png`; training `exec-008642e3-a032-478c-8b28-541cd4e9cd35.png`.

Shared generation direction: original wide 3:1 TWO-DIMENSIONAL editorial CARTOON illustration for a mature sports programme, clean slightly organic dark ink outlines, large simple flat coloured shapes, just one flat shadow tone, very little detail, no glossy AI cinematic rendering, photographic textures, glow, sunset, faces, people, words, numbers, logos, interface or frame. Restrained navy, cream, sage/forest green and sport accent palette. Left half almost empty dark desaturated navy for menu labels, subject filling right half with breathing room. Slight asymmetry and confident editorial linework, charming but not silly.

Sport-specific prompts: AFL has a recognisable red Australian oval footy with a simple stitched seam resting on muted green turf, four white goalposts with the inner pair taller, and a few cream field markings. Cricket has a warm cream bat beside three stumps and two bails, a modest red ball with a drawn seam at their base, and a sage strip with cream crease marks. Training has three simple orange cones at different distances, cream running-lane markings and a small navy/cream agility ladder.


### Exact v2 generation prompts

AFL:
> Create an original wide 3:1 TWO-DIMENSIONAL editorial CARTOON illustration for an Australian rules football menu card. It must look like considered human illustration for a sports programme, NOT a glossy AI cinematic render. Restrained mature cartoon style: clean slightly organic dark ink outlines, large simple flat coloured shapes, just one flat shadow tone, very little detail, no gradients within objects, no painterly cloud detail, no glow, no realistic textures, no 3D rendering, no sunset. A recognisable red Australian oval footy with simple stitched seam rests on the RIGHT on muted green oval turf. Four simple white goalposts behind it, inner pair taller; a few cream field markings. View at an easy side angle. Background plain dark desaturated navy, with the LEFT HALF kept almost empty dark navy for app text. Palette red, sage/forest green, cream, navy. Slight asymmetry and charming confident linework, sophisticated and clear rather than silly, no smiling faces, no people. No words, no numbers, no brand logos, no interface, no frame. Sport scene should occupy right half with generous breathing room; not a little icon.

Cricket:
> Create an original wide 3:1 TWO-DIMENSIONAL editorial CARTOON illustration for a cricket menu card, matching a restrained illustrated sports programme. NOT glossy AI cinematic art. Clean slightly organic dark ink outlines, large simple flat coloured shapes, just one flat shadow tone, very little detail. NO 3D rendering, NO photographic textures, NO glow, NO sunsets, NO detailed stadium or clouds. On the RIGHT: a simple warm cream cricket bat resting beside three cream stumps and two bails, with a modest red cricket ball and one drawn seam at the base, muted sage green cricket strip and a couple of cream crease marks. Plain dark desaturated navy background, LEFT HALF almost empty dark navy for app text. Palette warm cream, red, sage/forest green and navy. Slight asymmetry, confident editorial cartoon linework, mature and charming, not silly, no faces or people. No words, no numbers, no logos, no UI, no border. Subject fills right half naturally rather than appearing as a little icon.

Training:
> Create an original wide 3:1 TWO-DIMENSIONAL editorial CARTOON illustration for a sports training menu card. Restrained mature cartoon sports-programme style, NOT glossy AI cinematic art. Clean slightly organic dark ink outlines and large simple flat coloured shapes, just one flat shadow tone, very little detail. NO 3D render, NO photorealistic textures, NO glow, NO sunset or dramatic clouds. On the RIGHT half: three simple orange training cones arranged at different distances on muted sage/forest green turf, a couple of cream running-lane markings and a small simple navy/cream agility ladder. Plain dark desaturated navy background; LEFT HALF almost empty dark navy for app text. Palette orange, sage green, cream and navy. Charming confident imperfect editorial ink linework, clear and adult, not silly, no faces or people. No letters, numbers, logos, interface or borders. Training scene should fill right half with breathing room, not look like a tiny icon.
