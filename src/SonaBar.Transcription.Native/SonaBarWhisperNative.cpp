#include <windows.h>
#include <objbase.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <sstream>
#include <string>
#include <whisper.h>

struct sf_whisper_context { whisper_context* context = nullptr; std::atomic<bool> cancel_requested = false; std::mutex gate; };

static void set_error(char** error, const char* message) { if (!error) return; const auto length = std::strlen(message) + 1; *error = static_cast<char*>(CoTaskMemAlloc(length)); if (*error) std::memcpy(*error, message, length); }
static bool should_abort(void* user_data) { return static_cast<sf_whisper_context*>(user_data)->cancel_requested.load(); }
static std::string escape_json(const char* value) {
    std::ostringstream escaped;
    for (const unsigned char c : std::string(value ? value : "")) {
        switch (c) { case '\\': escaped << "\\\\"; break; case '"': escaped << "\\\""; break; case '\n': escaped << "\\n"; break; case '\r': escaped << "\\r"; break; case '\t': escaped << "\\t"; break;
        default: if (c < 0x20) { char buffer[7]; std::snprintf(buffer, sizeof(buffer), "\\u%04x", c); escaped << buffer; } else escaped << c; }
    }
    return escaped.str();
}

extern "C" __declspec(dllexport) sf_whisper_context* sf_whisper_load(const char* model_path, bool use_gpu, char** error) {
    try { auto parameters = whisper_context_default_params(); parameters.use_gpu = use_gpu; auto* context = whisper_init_from_file_with_params(model_path, parameters); if (!context) { set_error(error, "whisper.cpp could not load the selected model."); return nullptr; } auto* handle = new sf_whisper_context(); handle->context = context; return handle; }
    catch (const std::exception& exception) { set_error(error, exception.what()); return nullptr; } catch (...) { set_error(error, "whisper.cpp failed while loading the selected model."); return nullptr; }
}
extern "C" __declspec(dllexport) void sf_whisper_unload(sf_whisper_context* handle) { if (!handle) return; whisper_free(handle->context); delete handle; }
extern "C" __declspec(dllexport) void sf_whisper_cancel(sf_whisper_context* handle) { if (handle) handle->cancel_requested.store(true); }
extern "C" __declspec(dllexport) void sf_whisper_free_string(char* value) { CoTaskMemFree(value); }
extern "C" __declspec(dllexport) int sf_whisper_transcribe(sf_whisper_context* handle, const float* samples, int sample_count, const char* language, const char* prompt, int threads, char** result_json, char** error) {
    if (!handle || !samples || sample_count <= 0 || !result_json) { set_error(error, "Invalid whisper.cpp transcription request."); return -1; }
    std::lock_guard lock(handle->gate); handle->cancel_requested.store(false);
    auto parameters = whisper_full_default_params(WHISPER_SAMPLING_BEAM_SEARCH);
    parameters.n_threads = threads > 0 ? threads : 1;
    parameters.initial_prompt = prompt && *prompt ? prompt : nullptr; parameters.no_speech_thold = 0.6f; parameters.temperature = 0.0f; parameters.temperature_inc = 0.2f; parameters.beam_search.beam_size = 2; parameters.print_progress = false; parameters.print_realtime = false; parameters.print_timestamps = false; parameters.abort_callback = should_abort; parameters.abort_callback_user_data = handle;
    std::string resolved_language;
    if (language && std::strcmp(language, "auto") != 0) resolved_language = language;
    else resolved_language = "auto";
    // whisper.cpp runs its language detection inside the same call when the language is
    // "auto". Setting detect_language would make it return right after the detection and
    // skip the transcription completely, so it stays off.
    parameters.language = resolved_language.c_str();
    parameters.detect_language = false;
    const int status = whisper_full(handle->context, parameters, samples, sample_count);
    if (handle->cancel_requested.load()) return 1;
    if (status != 0) { set_error(error, "whisper.cpp could not transcribe this audio."); return status; }
    std::ostringstream json; const char* detected = whisper_lang_str(whisper_full_lang_id(handle->context)); json << "{\"language\":\"" << escape_json(detected) << "\",\"segments\":[";
    const int count = whisper_full_n_segments(handle->context);
    for (int index = 0; index < count; ++index) { if (index) json << ','; json << "{\"startMs\":" << whisper_full_get_segment_t0(handle->context, index) * 10 << ",\"endMs\":" << whisper_full_get_segment_t1(handle->context, index) * 10 << ",\"text\":\"" << escape_json(whisper_full_get_segment_text(handle->context, index)) << "\"}"; }
    json << "]}"; const auto output = json.str(); *result_json = static_cast<char*>(CoTaskMemAlloc(output.size() + 1)); if (!*result_json) { set_error(error, "SonaBar could not allocate the transcription result."); return -2; } std::memcpy(*result_json, output.c_str(), output.size() + 1); return 0;
}
