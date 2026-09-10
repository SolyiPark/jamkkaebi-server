package com.example.jamkkaebi.user.service;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.stereotype.Service;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.TransactionDefinition;
import org.springframework.transaction.support.TransactionTemplate;

import java.util.Locale;
import java.util.Objects;
import java.util.Optional;

/**
 * 소셜 프로필을 받아 잠깨비 사용자를 찾거나 만든다.
 *
 * <p>가입은 <b>닉네임 정리 → 친구 코드 발급 → User 저장</b>을 한 트랜잭션에서 처리한다. 셋을
 * 쪼개면 친구 코드가 없는 사용자 행이 남을 수 있는데, 친구 코드는 유일한 외부 식별자라 그 계정은
 * 아무 API 도 통과하지 못한다.
 */
@Service
public class UserRegistrationService {

    private static final Logger log = LoggerFactory.getLogger(UserRegistrationService.class);

    /** 친구 코드 충돌 시 재시도 횟수. 31^8 중 한 번 겹치는 일이 연속 5회 나기는 사실상 불가능하다. */
    private static final int FRIEND_CODE_MAX_ATTEMPTS = 5;
    /** users 의 친구 코드 유니크 제약 이름. 소문자 비교용. */
    private static final String FRIEND_CODE_UNIQUE_CONSTRAINT = "uk_users_friend_code";

    private final UserRepository userRepository;
    private final NicknamePolicy nicknamePolicy;
    private final FriendCodeGenerator friendCodeGenerator;
    private final TransactionTemplate registrationTransaction;

    public UserRegistrationService(UserRepository userRepository,
                                   NicknamePolicy nicknamePolicy,
                                   FriendCodeGenerator friendCodeGenerator,
                                   PlatformTransactionManager transactionManager) {
        this.userRepository = userRepository;
        this.nicknamePolicy = nicknamePolicy;
        this.friendCodeGenerator = friendCodeGenerator;
        this.registrationTransaction = new TransactionTemplate(transactionManager);
        this.registrationTransaction.setPropagationBehavior(
                TransactionDefinition.PROPAGATION_REQUIRES_NEW);
    }

    /**
     * 로그인 한 번의 결과.
     *
     * <p>가입 여부를 함께 돌려주는 이유는, <b>이 시점 말고는 신규 계정을 알아볼 방법이 없기</b>
     * 때문이다. 가입 직후 닉네임 수정 화면을 띄우려면 클라이언트가 그 사실을 알아야 하는데,
     * 나중에 {@code createdAt} 을 보고 "최근에 만들어졌으니 신규"라고 추측하는 방식은 재로그인과
     * 첫 로그인을 구분하지 못한다.
     *
     * @param user    찾았거나 방금 만든 사용자
     * @param newUser 이번 호출에서 새로 가입했는지
     */
    public record Registration(User user, boolean newUser) {
    }

    /**
     * 소셜 계정에 연결된 사용자를 찾고, 없으면 가입시킨다.
     *
     * <p>같은 계정의 <b>최초 로그인이 동시에</b> 들어오면(브라우저를 두 번 열었다든지) 양쪽 모두
     * "없음"을 보고 가입을 시도한다. 늦은 쪽은 {@code uk_users_provider_user} 에서 걸리는데, 그건
     * 오류가 아니라 <b>본인 계정이 방금 만들어졌다</b>는 뜻이다. 다시 조회해 멱등 성공으로 잇는다.
     * 이때 {@code newUser} 는 거짓이다 — 실제로 행을 만든 쪽에서 이미 참으로 알렸으므로, 여기서도
     * 참을 주면 같은 계정에 수정 화면이 두 번 뜬다.
     */
    public Registration findOrRegister(AuthProvider provider, String providerUserId, String rawNickname) {
        Optional<User> existing =
                userRepository.findByProviderAndProviderUserId(provider, providerUserId);
        if (existing.isPresent()) {
            return new Registration(existing.get(), false);
        }
        try {
            return new Registration(Objects.requireNonNull(registrationTransaction.execute(
                    status -> register(provider, providerUserId, rawNickname))), true);
        } catch (DataIntegrityViolationException exception) {
            User raced = userRepository.findByProviderAndProviderUserId(provider, providerUserId)
                    .orElseThrow(() -> exception);
            return new Registration(raced, false);
        }
    }

    private User register(AuthProvider provider, String providerUserId, String rawNickname) {
        String nickname = nicknamePolicy.sanitize(rawNickname);

        for (int attempt = 1; attempt <= FRIEND_CODE_MAX_ATTEMPTS; attempt++) {
            try {
                return userRepository.saveAndFlush(User.builder()
                        .provider(provider)
                        .providerUserId(providerUserId)
                        .friendCode(friendCodeGenerator.generate())
                        .nickname(nickname)
                        .build());
            } catch (DataIntegrityViolationException exception) {
                // 친구 코드가 겹친 경우에만 다시 뽑는다. provider 유니크 위반은 "이미 가입된 계정"
                // 이라 바깥의 재조회로 넘겨야 한다.
                if (!isFriendCodeCollision(exception) || attempt == FRIEND_CODE_MAX_ATTEMPTS) {
                    throw exception;
                }
                log.warn("친구 코드가 충돌해 다시 발급합니다. attempt={}", attempt);
            }
        }
        throw new BusinessException(ErrorCode.INTERNAL_SERVER_ERROR);
    }

    /**
     * 친구 코드 유니크 제약 위반인지 본다.
     *
     * <p>드라이버·DB 에 따라 제약 이름이 최상위 메시지가 아니라 원인 예외 체인에만 담기므로 전체를
     * 훑는다. 이름 비교는 대소문자를 구분하지 않는다.
     */
    private boolean isFriendCodeCollision(DataIntegrityViolationException exception) {
        for (Throwable cause = exception; cause != null; cause = cause.getCause()) {
            String message = cause.getMessage();
            if (message != null
                    && message.toLowerCase(Locale.ROOT).contains(FRIEND_CODE_UNIQUE_CONSTRAINT)) {
                return true;
            }
        }
        return false;
    }
}
