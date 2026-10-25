cmake_minimum_required(VERSION 3.26)
project(SonaBarParakeetNative LANGUAGES C CXX)

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

# NeMo-Speech resolves SentencePiece with find_library(). With the Visual Studio
# multi-config generator that lookup can select vcpkg's debug/lib archive even
# for a Release build, which then fails at link time with MDd/MD mismatches.
# Pin the release archive explicitly so every configuration uses the same CRT.
if(WIN32 AND DEFINED VCPKG_INSTALLED_DIR AND DEFINED VCPKG_TARGET_TRIPLET)
    set(_sentencepiece_root "${VCPKG_INSTALLED_DIR}/${VCPKG_TARGET_TRIPLET}")
    set(SENTENCEPIECE_LIB "${_sentencepiece_root}/lib/sentencepiece.lib" CACHE FILEPATH "Release SentencePiece static library" FORCE)
    set(SENTENCEPIECE_INCLUDE_DIR "${_sentencepiece_root}/include" CACHE PATH "SentencePiece headers" FORCE)
    if(NOT EXISTS "${SENTENCEPIECE_LIB}")
        message(FATAL_ERROR "SentencePiece release library not found at ${SENTENCEPIECE_LIB}")
    endif()
endif()

add_subdirectory(${CMAKE_CURRENT_LIST_DIR}/../../third_party/NeMo-Speech.cpp ${CMAKE_CURRENT_BINARY_DIR}/nemo-speech)

# NeMo's C ABI target points its build interface at ${CMAKE_SOURCE_DIR}/include,
# which is correct when NeMo is the top-level project. In this wrapper the public
# headers live in the submodule, so add that directory explicitly.
target_include_directories(nemo_speech_asr_c PUBLIC
    "${CMAKE_CURRENT_LIST_DIR}/../../third_party/NeMo-Speech.cpp/include")

# The vcpkg SentencePiece package references Abseil's flag implementation.
# vcpkg ships that component as separate static libraries and its exported
# target list does not include them, so NeMo's ASR DLL would otherwise fail
# with unresolved absl::flags_internal symbols. Link the archives directly.
if(WIN32)
    file(GLOB ABSL_FLAG_LIBS
        "${VCPKG_INSTALLED_DIR}/${VCPKG_TARGET_TRIPLET}/lib/absl_flags_*.lib")
    if(ABSL_FLAG_LIBS)
        target_link_libraries(nemo_speech_asr PRIVATE ${ABSL_FLAG_LIBS})
    else()
        message(WARNING "No Abseil flag libraries found below ${VCPKG_INSTALLED_DIR}/${VCPKG_TARGET_TRIPLET}/lib")
    endif()
endif()
