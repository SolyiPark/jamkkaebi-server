package com.example.jamkkaebi.auth.service;

/**
 * 소셜에서 가져오는 값 전부. <b>회원번호와 닉네임 둘뿐</b>이다.
 *
 * <p>이메일과 프로필 사진은 스코프·동의항목에서 아예 빼 두었으므로 응답에 오지 않고, 와도 읽지
 * 않는다. 프로필 이미지는 사전 제작 아바타로 대신한다.
 *
 * @param providerUserId 소셜 서비스 고유 회원번호
 * @param rawNickname    제공자가 준 이름 ({@code null} 가능). 그대로 저장하지 않고 닉네임 정책을 태운다.
 */
public record SocialProfile(String providerUserId, String rawNickname) {
}
