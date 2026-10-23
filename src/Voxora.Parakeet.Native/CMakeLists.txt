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
