package com.example.jamkkaebi.user.dto.request;

/**
 * 프로필 아바타 변경 요청.
 *
 * @param spiritId 아바타로 쓸 도깨비(= 유물 번호). {@code null} 이면 기본(사람 모양) 아바타로 되돌린다.
 *                 도깨비 이름 짓기와 같이 키를 빼도 {@code null} 로 본다
 */
public record AvatarUpdateRequest(Integer spiritId) {
}
