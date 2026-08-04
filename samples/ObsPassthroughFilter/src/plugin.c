#include <obs-module.h>

#define FOUNDRY_FILTER_ID "dev.creatorsforge.passthrough-filter"

static const char *foundry_filter_name(void *type_data)
{
    UNUSED_PARAMETER(type_data);
    return "Foundry Passthrough Filter v0.2";
}

struct foundry_filter_context {
    obs_source_t *source;
};

static void *foundry_filter_create(obs_data_t *settings, obs_source_t *source)
{
    UNUSED_PARAMETER(settings);
    struct foundry_filter_context *context = bzalloc(sizeof(*context));
    context->source = source;
    return context;
}

static void foundry_filter_destroy(void *data)
{
    bfree(data);
}

static void foundry_filter_render(void *data, gs_effect_t *effect)
{
    UNUSED_PARAMETER(effect);
    struct foundry_filter_context *context = data;
    obs_source_skip_video_filter(context->source);
}

static struct obs_source_info foundry_filter = {
    .id = FOUNDRY_FILTER_ID,
    .type = OBS_SOURCE_TYPE_FILTER,
    .output_flags = OBS_SOURCE_VIDEO,
    .get_name = foundry_filter_name,
    .create = foundry_filter_create,
    .destroy = foundry_filter_destroy,
    .video_render = foundry_filter_render,
};

bool foundry_obs_plugin_load(void)
{
    obs_register_source(&foundry_filter);
    blog(LOG_INFO, "registered %s", FOUNDRY_FILTER_ID);
    return true;
}
