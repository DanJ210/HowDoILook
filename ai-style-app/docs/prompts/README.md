# Prompts

Place AI prompt templates and model guidance notes here as `.md` files.

Current generation flow primarily uses structured Replicate inputs (`haircut`, `hairColor`, `gender`, `input_image`) rather than a single free-form prompt.
`prompt` is still accepted by the API for compatibility/fallback metadata.

Example structure:
- `style-analysis.md` - optional analysis prompt templates
- `style-recommendation.md` - optional recommendation prompt templates
- `replicate-change-haircut.md` - canonical model behavior notes for `flux-kontext-apps/change-haircut`
