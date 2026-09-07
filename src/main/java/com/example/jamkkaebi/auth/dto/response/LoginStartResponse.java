package com.example.jamkkaebi.auth.dto.response;

/**
 * 로그인 시작 응답.
 *
 * @param authorizeUrl 클라이언트가 시스템 브라우저로 열어야 할 주소.
 *                     이 주소를 열면 서버가 {@code state} 를 만들어 소셜 동의 화면으로 넘긴다.
 */
public record LoginStartResponse(String authorizeUrl) {
}
