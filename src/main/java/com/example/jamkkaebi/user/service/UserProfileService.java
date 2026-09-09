package com.example.jamkkaebi.user.service;

import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * 로그인한 사용자가 자기 프로필을 읽고 고치는 경로.
 *
 * <p>인증 컨텍스트에는 친구 코드밖에 없으므로, 여기서 유니크 인덱스 조회 한 번으로 사용자를 찾는다.
 * 그 대가로 내부 순번이 토큰에 실려 나가지 않는다.
 */
@Service
public class UserProfileService {

    private final UserRepository userRepository;
    private final NicknamePolicy nicknamePolicy;

    public UserProfileService(UserRepository userRepository, NicknamePolicy nicknamePolicy) {
        this.userRepository = userRepository;
        this.nicknamePolicy = nicknamePolicy;
    }

    @Transactional(readOnly = true)
    public User getByFriendCode(String friendCode) {
        return userRepository.findByFriendCode(friendCode)
                .orElseThrow(() -> new BusinessException(ErrorCode.USER_NOT_FOUND));
    }

    /**
     * 닉네임을 바꾼다.
     *
     * <p>가입 직후 수정 화면이 부르는 자리다. 소셜에서 받은 이름은 10자로 잘리거나 기본값으로
     * 바뀐 값이라 사용자가 고른 이름이 아니고, 그대로 굳으면 친구 목록·전시관에 계속 노출된다.
     */
    @Transactional
    public User updateNickname(String friendCode, String nickname) {
        User user = userRepository.findByFriendCode(friendCode)
                .orElseThrow(() -> new BusinessException(ErrorCode.USER_NOT_FOUND));
        user.updateNickname(nicknamePolicy.validate(nickname));
        return user;
    }
}
