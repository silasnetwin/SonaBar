cmake_minimum_required(VERSION 3.26)
project(SineFlowParakeetNative LANGUAGES C CXX)

set(NEMO_SPEECH_GGML_PATCHED OFF CACHE BOOL "" FORCE)
set(NEMO_SPEECH_BUILD_ASR ON CACHE BOOL "" FORCE)
set(NEMO_SPEECH_BUILD_DIAR OFF CACHE BOOL "" FORCE)
set(NEMO_SPEECH_BUILD_TTS OFF CACHE BOOL "" FORCE)
set(NEMO_SPEECH_BUILD_NMT OFF CACHE BOOL "" FORCE)
set(NEMO_SPEECH_BUILD_CLI OFF CACHE BOOL "" FORCE)
set(NEMO_SPEECH_BUILD_MIC_CAPTURE OFF CACHE BOOL "" FORCE)
set(NEMO_SPEECH_BUILD_HTTP OFF CACHE BOOL "" FORCE)
set(NEMO_SPEECH_BUILD_GRPC OFF CACHE BOOL "" FORCE)
set(NEMO_SPEECH_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(NEMO_SPEECH_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
add_subdirectory(${CMAKE_CURRENT_LIST_DIR}/../../third_party/NeMo-Speech.cpp ${CMAKE_CURRENT_BINARY_DIR}/nemo-speech)

# The vcpkg SentencePiece package links Abseil's flag implementation as a
# static dependency. NeMo v0.1.0 links the container targets but not this
# required transitive group on Windows, so retain it in SineFlow's closure.
if(WIN32 AND TARGET absl::flags)
    target_link_libraries(nemo_speech_asr PRIVATE
        absl::flags absl::flags_parse absl::flags_usage absl::flags_usage_internal
        absl::flags_internal absl::flags_reflection absl::flags_commandlineflag
        absl::flags_commandlineflag_internal absl::flags_private_handle_accessor
        absl::flags_marshalling absl::flags_program_name absl::flags_config absl::flags_path_util)
endif()
