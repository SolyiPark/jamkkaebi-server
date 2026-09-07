package com.example.jamkkaebi.auth.service;

import com.example.jamkkaebi.auth.dto.response.AuthTokenResponse;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.security.JwtProvider;
import com.example.jamkkaebi.global.security.TokenType;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * 인계 코드 교환, 토큰 재발급, 로그아웃을 담당한다.
 *
 * <p>소셜 인증 자체는 Spring Security OAuth2 Client 와 성공 핸들러가 처리하고, 이 서비스는
 * <b>잠깨비 토큰</b>만 다룬다. 소셜 제공자가 준 Access Token 은 신원 확인에만 쓰고 여기까지 오지
 * 않는다.
 */
@Service
public class AuthService {

    private final HandoffService handoffService;
    private final UserRepository userRepository;
    private final JwtProvider jwtProvider;
    private final RefreshTokenService refreshTokenService;

    public AuthService(HandoffService handoffService,
                       UserRepository userRepository,
                       JwtProvider jwtProvider,
                       RefreshTokenService refreshTokenService) {
        this.handoffService = handoffService;
        this.userRepository = userRepository;
        this.jwtProvider = jwtProvider;
        this.refreshTokenService = refreshTokenService;
    }

    /**
     * 딥링크로 받은 인계 코드를 잠깨비 토큰으로 바꾼다.
     *
     * <p>코드를 가로챈 쪽은 {@code verifier} 원문을 모르므로 여기서 걸린다 — 그것이 인계 코드와
     * 토큰을 두 단계로 나눈 이유다.
     */
    @Transactional
    public AuthTokenResponse exchange(String handoffCode, String verifier, String deviceLabel) {
        Long userId = handoffService.exchange(handoffCode, verifier);
        User user = userRepository.findById(userId)
                .orElseThrow(() -> new BusinessException(ErrorCode.USER_NOT_FOUND));

        String accessToken = jwtProvider.createAccessToken(user.getFriendCode());
        String refreshToken = jwtProvider.createRefreshToken(user.getFriendCode());
        refreshTokenService.save(
                userId, refreshToken, jwtProvider.getRefreshTokenValidityMs(), deviceLabel);

        return new AuthTokenResponse(
                accessToken, refreshToken, user.getFriendCode(), user.getNickname());
    }

    /**
     * Refresh Token 을 검증하고 Access/Refresh 를 함께 재발급한다. (회전)
     *
     * <p>실패 사유(없음·만료·재사용)를 모두 {@code A005} 로 돌려준다 — 클라이언트가 할 일은 어느
     * 경우든 재로그인 하나뿐이고, 나누면 "그 토큰은 폐기됐다"는 사실이 새어 나간다.
     */
    @Transactional
    public AuthTokenResponse reissue(String refreshToken, String deviceLabel) {
        String friendCode = jwtProvider.parseFriendCode(refreshToken, TokenType.REFRESH);
        User user = userRepository.findByFriendCode(friendCode)
                .orElseThrow(() -> new BusinessException(ErrorCode.INVALID_REFRESH_TOKEN));

        String newAccessToken = jwtProvider.createAccessToken(friendCode);
        String newRefreshToken = jwtProvider.createRefreshToken(friendCode);

        RefreshTokenService.RotationResult result = refreshTokenService.rotate(
                user.getId(), refreshToken, newRefreshToken,
                jwtProvider.getRefreshTokenValidityMs(), deviceLabel);

        if (result != RefreshTokenService.RotationResult.ROTATED) {
            throw new BusinessException(ErrorCode.INVALID_REFRESH_TOKEN);
        }
        return new AuthTokenResponse(
                newAccessToken, newRefreshToken, friendCode, user.getNickname());
    }

    /** 이 사용자의 Refresh Token 을 모두 폐기한다. */
    @Transactional
    public void logout(String friendCode) {
        userRepository.findByFriendCode(friendCode)
                .ifPresent(user -> refreshTokenService.revokeAll(user.getId()));
    }
}
