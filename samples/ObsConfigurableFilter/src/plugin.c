#include <obs-module.h>

#define FOUNDRY_FILTER_ID "dev.creatorsforge.configurable-filter"

struct foundry_filter_context {
    obs_source_t *source;
    bool enabled;
};

static const char *foundry_filter_name(void *unused)
{
    UNUSED_PARAMETER(unused);
    return "Foundry Configurable Filter";
}

static void foundry_filter_update(void *data, obs_data_t *settings)
{
    struct foundry_filter_context *context = data;
    context->enabled = obs_data_get_bool(settings, "enabled");
}

static void *foundry_filter_create(obs_data_t *settings, obs_source_t *source)
{
    struct foundry_filter_context *context = bzalloc(sizeof(*context));
    context->source = source;
    foundry_filter_update(context, settings);
    return context;
}

static void foundry_filter_destroy(void *data)
{
    bfree(data);
}

static void foundry_filter_defaults(obs_data_t *settings)
{
    obs_data_set_default_bool(settings, "enabled", true);
}

static obs_properties_t *foundry_filter_properties(void *data)
{
    UNUSED_PARAMETER(data);
    obs_properties_t *properties = obs_properties_create();
    obs_properties_add_bool(properties, "enabled", "Enabled");
    return properties;
}

static void foundry_filter_render(void *data, gs_effect_t *effect)
{
    UNUSED_PARAMETER(effect);
    struct foundry_filter_context *context = data;
    if (!context->enabled)
        obs_source_skip_video_filter(context->source);
    else
        obs_source_skip_video_filter(context->source);
}

static struct obs_source_info foundry_filter_info = {
    .id = FOUNDRY_FILTER_ID,
    .type = OBS_SOURCE_TYPE_FILTER,
    .output_flags = OBS_SOURCE_VIDEO,
    .get_name = foundry_filter_name,
    .create = foundry_filter_create,
    .destroy = foundry_filter_destroy,
    .update = foundry_filter_update,
    .get_defaults = foundry_filter_defaults,
    .get_properties = foundry_filter_properties,
    .video_render = foundry_filter_render,
};

bool foundry_obs_plugin_load(void)
{
    obs_register_source(&foundry_filter_info);
    blog(LOG_INFO, "[Creators Forge] Registered %s.", FOUNDRY_FILTER_ID);
    return true;
}

