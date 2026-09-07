package com.example.jamkkaebi.user.service;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 자동 가입을 검증한다. 로그인이 게임의 유일한 진입로라, 여기서 막히면 사용자가 들어올 방법이 없다.
 */
@SpringBootTest
class UserRegistrationServiceTest {

    @Autowired
    private UserRegistrationService userRegistrationService;

    @Autowired
    private UserRepository userRepository;

    @AfterEach
    void clear() {
        userRepository.deleteAll();
    }

    @Test
    @DisplayName("최초 로그인이면 친구 코드를 발급하며 자동 가입시킨다")
    void registersOnFirstLogin() {
        User user = userRegistrationService.findOrRegister(AuthProvider.GOOGLE, "g-1001", "잠깨비");

        assertThat(user.getId()).isNotNull();
        assertThat(user.getFriendCode()).hasSize(User.FRIEND_CODE_LENGTH);
        assertThat(user.getNickname()).isEqualTo("잠깨비");
    }

    @Test
    @DisplayName("이메일과 프로필 사진은 저장하지 않는다")
    void neverStoresEmail() {
        User user = userRegistrationService.findOrRegister(AuthProvider.KAKAO, "k-1002", "잠깨비");

        assertThat(user.getEmail()).isNull();
        assertThat(user.getSpiritId()).isNull();
    }

    @Test
    @DisplayName("같은 소셜 계정으로 다시 로그인하면 같은 사용자다 — 친구 코드가 유지된다")
    void reusesExistingUser() {
        User first = userRegistrationService.findOrRegister(AuthProvider.GOOGLE, "g-1003", "잠깨비");
        User second = userRegistrationService.findOrRegister(AuthProvider.GOOGLE, "g-1003", "바뀐이름");

        assertThat(second.getId()).isEqualTo(first.getId());
        assertThat(second.getFriendCode()).isEqualTo(first.getFriendCode());
        // 재로그인은 소셜 닉네임을 다시 덮어쓰지 않는다 — 사용자가 바꾼 이름이 날아가면 안 된다.
        assertThat(second.getNickname()).isEqualTo("잠깨비");
        assertThat(userRepository.count()).isEqualTo(1);
    }

    @Test
    @DisplayName("회원번호가 같아도 제공자가 다르면 다른 계정이다 — 식별 기준이 조합이기 때문")
    void separatesAccountsByProvider() {
        User google = userRegistrationService.findOrRegister(AuthProvider.GOOGLE, "same-id", "구글잠깨비");
        User kakao = userRegistrationService.findOrRegister(AuthProvider.KAKAO, "same-id", "카카오잠깨비");

        assertThat(kakao.getId()).isNotEqualTo(google.getId());
        assertThat(kakao.getFriendCode()).isNotEqualTo(google.getFriendCode());
    }

    @Test
    @DisplayName("10자를 넘는 소셜 닉네임 때문에 가입이 실패하지 않는다")
    void survivesOverlongNickname() {
        User user = userRegistrationService.findOrRegister(
                AuthProvider.KAKAO, "k-1004", "열두글자가넘어가는아주긴닉네임");

        assertThat(user.getNickname().codePointCount(0, user.getNickname().length()))
                .isLessThanOrEqualTo(User.NICKNAME_MAX_LENGTH);
    }

    @Test
    @DisplayName("닉네임 동의를 받지 못해도 가입이 완료된다")
    void survivesMissingNickname() {
        User user = userRegistrationService.findOrRegister(AuthProvider.KAKAO, "k-1005", null);

        assertThat(user.getNickname()).isEqualTo(NicknamePolicy.DEFAULT_NICKNAME);
    }

    @Test
    @DisplayName("사용자마다 친구 코드가 겹치지 않는다")
    void issuesDistinctFriendCodes() {
        User a = userRegistrationService.findOrRegister(AuthProvider.GOOGLE, "g-2001", "가");
        User b = userRegistrationService.findOrRegister(AuthProvider.GOOGLE, "g-2002", "나");

        assertThat(a.getFriendCode()).isNotEqualTo(b.getFriendCode());
    }
}
